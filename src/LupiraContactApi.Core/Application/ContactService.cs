using LupiraContactApi.Auth;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using LupiraContactApi.Mappers;
using LupiraContactApi.Serialization;
using Marten;

namespace LupiraContactApi.Application;

/// <summary>Contact core shared by REST, CardDAV, and MCP. Event-sourced like <see cref="CalendarItemService"/>; a contact belongs to one address book.</summary>
public sealed class ContactService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness)
{
    public async Task<OpResult<ContactDto>> CreateAsync(Guid principalId, CreateContactRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, r.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this address book.");

        var channels = ReachChannelNormalizer.Normalize(r.Channels ?? []);
        if (ReachChannelNormalizer.HasPreferredConflict(channels)) return OpResult<ContactDto>.Invalid("At most one preferred channel per medium.");
        Stamp(principalId);

        var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var fields = new ContactFields(r.GivenName, r.MiddleName, r.FamilyName, r.Nickname, channels, r.Birthday, r.Tags, r.Notes, r.Pronouns, r.DisplayNameFormat ?? DisplayNameFormat.Full);

        session.Events.StartStream<Contact>(id, new ContactCreated(id, r.AddressBookId, uid, fields));
        await session.SaveChangesAsync(ct);
        var c = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(c!, ct));
    }

    public async Task<OpResult<List<ContactDto>>> QueryAsync(Guid principalId, string? query, Guid? addressBookId, CancellationToken ct = default)
    {
        var bookIds = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        if (addressBookId is { } abid)
        {
            if (!bookIds.Contains(abid)) return OpResult<List<ContactDto>>.Forbidden("No access to this address book.");
            bookIds = [abid];
        }

        var candidates = await session.Query<Contact>().Where(c => c.DeletedAt == null).ToListAsync(ct);
        IEnumerable<Contact> contacts = candidates.Where(c => bookIds.Contains(c.AddressBookId));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            contacts = contacts.Where(c => c.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var ordered = contacts.OrderBy(c => c.SortName).ToList();
        var scores = await completeness.ScoreContactsAsync(ordered, ct);
        return OpResult<List<ContactDto>>.Ok([.. ordered.Select(c => c.ToResponse(scores[c.Id]))]);
    }

    public const int MaxBatch = 100;

    /// <summary>Create many contacts in one unit of work; returns them in input order. Fails the whole batch on any
    /// forbidden book or channel conflict (nothing is committed). Mirrors <see cref="CreateAsync"/> per item.</summary>
    public async Task<OpResult<List<ContactDto>>> CreateBatchAsync(Guid principalId, IReadOnlyList<CreateContactRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return OpResult<List<ContactDto>>.Invalid("At least one contact is required.");
        if (requests.Count > MaxBatch) return OpResult<List<ContactDto>>.Invalid($"At most {MaxBatch} contacts per batch.");

        foreach (var abid in requests.Select(r => r.AddressBookId).Distinct())
            if (!await access.CanWriteAddressBookAsync(principalId, abid, ct))
                return OpResult<List<ContactDto>>.Forbidden($"No write access to address book {abid}.");

        Stamp(principalId);
        var ids = new List<Guid>(requests.Count);
        foreach (var r in requests)
        {
            var channels = ReachChannelNormalizer.Normalize(r.Channels ?? []);
            if (ReachChannelNormalizer.HasPreferredConflict(channels))
                return OpResult<List<ContactDto>>.Invalid($"Contact '{r.GivenName} {r.FamilyName}': at most one preferred channel per medium.");
            var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
            var id = DeterministicGuid.From(uid);
            var fields = new ContactFields(r.GivenName, r.MiddleName, r.FamilyName, r.Nickname, channels, r.Birthday, r.Tags, r.Notes, r.Pronouns, r.DisplayNameFormat ?? DisplayNameFormat.Full);
            session.Events.StartStream<Contact>(id, new ContactCreated(id, r.AddressBookId, uid, fields));
            ids.Add(id);
        }
        await session.SaveChangesAsync(ct);

        var loaded = (await session.Query<Contact>().Where(c => ids.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
        var ordered = ids.Select(i => loaded[i]).ToList();
        var scores = await completeness.ScoreContactsAsync(ordered, ct);
        return OpResult<List<ContactDto>>.Ok([.. ordered.Select(c => c.ToResponse(scores[c.Id]))]);
    }

    /// <summary>Batch-match input names to accessible contacts for import disambiguation. Per name: exactly one
    /// normalized-display-name equal (or lone substring hit) → Matched; several → Ambiguous; none → NotFound.
    /// Substring + normalized-name only (not phonetic). Candidates are capped.</summary>
    public async Task<OpResult<List<ContactNameMatch>>> ResolveByNameAsync(Guid principalId, IReadOnlyList<string> names, Guid? addressBookId, CancellationToken ct = default)
    {
        if (names.Count == 0) return OpResult<List<ContactNameMatch>>.Invalid("At least one name is required.");
        if (names.Count > MaxBatch) return OpResult<List<ContactNameMatch>>.Invalid($"At most {MaxBatch} names per batch.");

        var bookIds = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        if (addressBookId is { } abid)
        {
            if (!bookIds.Contains(abid)) return OpResult<List<ContactNameMatch>>.Forbidden("No access to this address book.");
            bookIds = [abid];
        }
        var pool = (await session.Query<Contact>().Where(c => c.DeletedAt == null).ToListAsync(ct))
            .Where(c => bookIds.Contains(c.AddressBookId)).ToList();

        const int maxCandidates = 5;
        var results = new List<ContactNameMatch>(names.Count);
        foreach (var raw in names)
        {
            var query = (raw ?? "").Trim();
            var norm = Norm(query);
            if (norm.Length == 0)
            {
                results.Add(new ContactNameMatch { Name = raw ?? "", Outcome = NameMatchOutcome.NotFound, Candidates = [] });
                continue;
            }
            var candidates = pool.Where(c => c.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            var exact = candidates.Where(c => Norm(c.DisplayName) == norm).ToList();

            NameMatchOutcome outcome;
            Guid? matchId = null;
            List<Contact> refs;
            if (exact.Count == 1) { outcome = NameMatchOutcome.Matched; matchId = exact[0].Id; refs = exact; }
            else if (candidates.Count == 0) { outcome = NameMatchOutcome.NotFound; refs = []; }
            else { outcome = NameMatchOutcome.Ambiguous; refs = exact.Count > 1 ? exact : candidates; }

            results.Add(new ContactNameMatch
            {
                Name = raw ?? "",
                ContactId = matchId,
                Outcome = outcome,
                Candidates = [.. refs.OrderBy(c => c.SortName).Take(maxCandidates).Select(c => new ContactRef { ContactId = c.Id, DisplayName = c.DisplayName })],
            });
        }
        return OpResult<List<ContactNameMatch>>.Ok(results);
    }

    // Ported from LupiraAssistantApi ContactResolveStrategy.Norm: lowercase + collapse whitespace.
    private static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public async Task<OpResult<ContactDto>> GetAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to this contact.");
        return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
    }

    /// <summary>Merge-update an existing contact: provided scalars overwrite, provided email/phone/tag arrays
    /// union onto the existing values (deduped), null fields are kept. Never wipes unmentioned fields.</summary>
    public async Task<OpResult<ContactDto>> ReviseAsync(Guid principalId, Guid id, ReviseContactRequest r, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        Stamp(principalId);

        var merged = new ContactFields(
            r.GivenName ?? c.GivenName,
            r.MiddleName ?? c.MiddleName,
            r.FamilyName ?? c.FamilyName,
            r.Nickname ?? c.Nickname,
            MergeChannels(c.Channels, r.Channels),
            r.Birthday ?? c.Birthday,
            MergeDistinct(c.Tags, r.Tags),
            r.Notes ?? c.Notes,
            r.Pronouns ?? c.Pronouns,
            r.DisplayNameFormat ?? c.DisplayNameFormat);

        stream.AppendOne(new ContactRevised(id, merged));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    // Union an incoming multi-value field onto the existing one (case-insensitive dedupe); a null/empty
    // incoming keeps the existing values (enrichment adds, never clears).
    private static string[]? MergeDistinct(string[]? existing, string[]? incoming)
    {
        if (incoming is null || incoming.Length == 0) return existing;
        if (existing is null || existing.Length == 0) return incoming;
        return [.. existing.Concat(incoming).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    // Union incoming channels onto existing (enrichment — never clears): dedupe by (medium, value); a new value keeps
    // its preferred flag only if that medium has no preferred yet, so the merge can't create a preferred conflict.
    private static IReadOnlyList<ContactReachChannel> MergeChannels(IReadOnlyList<ContactReachChannel> existing, IReadOnlyList<ContactReachChannel>? incoming)
    {
        var inc = ReachChannelNormalizer.Normalize(incoming ?? []);
        if (inc.Count == 0) return existing;
        var result = existing.ToList();
        var have = result.Select(c => (c.Medium, V: c.Value.ToLowerInvariant())).ToHashSet();
        var preferredMedia = result.Where(c => c.Preferred).Select(c => c.Medium).ToHashSet();
        foreach (var ch in inc)
        {
            if (!have.Add((ch.Medium, ch.Value.ToLowerInvariant()))) continue;   // value already present — keep existing entry
            result.Add(ch with { Preferred = ch.Preferred && preferredMedia.Add(ch.Medium) });
        }
        return result;
    }

    // ---- Reach channels (emails + phones) + tags — the removable, wholesale counterpart to ReviseContact's union-merge ----

    public async Task<OpResult<ContactDto>> SetChannelsAsync(Guid principalId, Guid id, IReadOnlyList<ContactReachChannel> channels, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = ReachChannelNormalizer.Normalize(channels);
        if (ReachChannelNormalizer.HasPreferredConflict(next)) return OpResult<ContactDto>.Invalid("At most one preferred channel per medium.");
        if (ChannelsEqual(c.Channels, next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactRevised(id, FieldsOf(c) with { Channels = next }));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public Task<OpResult<ContactDto>> SetTagsAsync(Guid principalId, Guid id, string[] tags, CancellationToken ct = default) =>
        ReplaceMultiAsync(principalId, id, tags, c => c.Tags, (c, next) => FieldsOf(c) with { Tags = next }, ct);

    private async Task<OpResult<ContactDto>> ReplaceMultiAsync(Guid principalId, Guid id, string[] incoming,
        Func<Contact, string[]?> current, Func<Contact, string[], ContactFields> apply, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = NormalizeMulti(incoming);
        if (NormalizeMulti(current(c)).SequenceEqual(next, StringComparer.Ordinal)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // no event, no ETag churn
        Stamp(principalId);

        var merged = apply(c, next);
        stream.AppendOne(new ContactRevised(id, merged));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // Trim, drop blanks, de-duplicate case-insensitively (first casing wins). Order is preserved and significant.
    private static string[] NormalizeMulti(string[]? values) =>
        [.. (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult.Forbidden("No write access to this contact.");
        Stamp(principalId);
        stream.AppendOne(new ContactDeleted(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    // ---- Deceased (death is not deletion — the contact stays in the kinship graph) ----

    public async Task<OpResult<ContactDto>> SetDeceasedAsync(Guid principalId, Guid id, DateOnly? deathDate, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (c.Deceased && c.DeathDate == deathDate) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // no event, no ETag churn
        Stamp(principalId);

        stream.AppendOne(new ContactMarkedDeceased(id, deathDate));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public async Task<OpResult<ContactDto>> ClearDeceasedAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (!c.Deceased) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactDeceasedCleared(id));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // ---- Avatar (a pointer to an image, never bytes — outside the canonical content, like addresses) ----

    public async Task<OpResult<ContactDto>> SetAvatarAsync(Guid principalId, Guid id, string? avatarRef, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = string.IsNullOrWhiteSpace(avatarRef) ? null : avatarRef.Trim();
        if (c.AvatarRef == next) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactAvatarSet(id, next));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // ---- Profiles + addresses (wholesale replace) ----

    public async Task<OpResult<ContactDto>> SetProfilesAsync(Guid principalId, Guid id, IReadOnlyList<ContactSocialProfile> profiles, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var normalized = profiles.Select(SocialProfileNormalizer.Normalize).ToList();
        if (normalized.Any(p => p.Service.Length == 0 || p.Handle.Length == 0)) return OpResult<ContactDto>.Invalid("Profile service and handle are required.");
        var next = normalized.DistinctBy(p => (p.Service, Handle: p.Handle.ToLowerInvariant())).ToList();
        if (next.GroupBy(p => p.Service).Any(g => g.Count(p => p.Preferred) > 1)) return OpResult<ContactDto>.Invalid("At most one preferred handle per service.");
        if (ProfilesEqual(c.Profiles, next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactProfilesReplaced(id, next));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public async Task<OpResult<ContactDto>> SetAddressesAsync(Guid principalId, Guid id, IReadOnlyList<ContactPostalAddress> addresses, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = addresses.Select(a => new ContactPostalAddress { PlaceId = a.PlaceId, Type = a.Type }).ToList();
        if (next.Any(a => a.PlaceId is null || a.PlaceId == Guid.Empty)) return OpResult<ContactDto>.Invalid("Each address must reference a geo place id.");
        if (AddressesEqual(c.Addresses, next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactAddressesReplaced(id, next));   // addresses are outside the canonical content — ETag unchanged
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // ---- Emergency contacts (ordered designation, not a kinship) ----

    public async Task<OpResult<ContactDto>> SetEmergencyContactsAsync(Guid principalId, Guid id, IReadOnlyList<Guid> contactIds, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = contactIds.Distinct().ToList();
        if (next.Contains(id)) return OpResult<ContactDto>.Invalid("A contact cannot be its own emergency contact.");
        foreach (var targetId in next)
        {
            var target = await session.LoadAsync<Contact>(targetId, ct);
            if (target is null || target.DeletedAt is not null) return OpResult<ContactDto>.Invalid("Emergency contact not found.");
            if (!await access.CanReadAddressBookAsync(principalId, target.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to an emergency contact.");
        }
        if (c.EmergencyContactIds.SequenceEqual(next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactEmergencyContactsReplaced(id, next));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // ---- Relations (directed edges on the from-contact's stream; see ContactRelation) ----

    /// <summary>Upserts "<c>r.ToContactId</c> is this contact's <c>r.Kind</c>" (re-adding the same key revises the label and revives an ended edge).
    /// Requires write on this contact's book and read on the target's; the import path is deliberately laxer (no target check).
    /// Parent/child adds are refused when they would make someone their own ancestor. Sibling edges are stored as-is —
    /// shared-parentage siblinghood is derived on read (<see cref="KinshipInference"/>), never fabricated on write.</summary>
    public async Task<OpResult<ContactDto>> AddRelationAsync(Guid principalId, Guid id, AddContactRelationRequest r, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (r.ToContactId == id) return OpResult<ContactDto>.Invalid("A contact cannot relate to itself.");

        var target = await session.LoadAsync<Contact>(r.ToContactId, ct);
        if (target is null || target.DeletedAt is not null) return OpResult<ContactDto>.Invalid("Related contact not found.");
        if (!await access.CanReadAddressBookAsync(principalId, target.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to the related contact.");

        if (r.Kind is ContactRelationKind.Parent or ContactRelationKind.Child)
        {
            var (childId, parentId) = r.Kind == ContactRelationKind.Parent ? (id, r.ToContactId) : (r.ToContactId, id);
            var live = await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct);
            if (KinshipInference.WouldCreateParentCycle(childId, parentId, live))
                return OpResult<ContactDto>.Invalid("Would create a parentage cycle.");
        }

        var label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label.Trim();
        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        if (c.Relations.Any(x => x.ToContactId == r.ToContactId && x.Kind == r.Kind && x.Label == label && x.Since == r.Since && x.Note == note && !x.Ended))
            return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // identical live edge: no event, no ETag churn
        Stamp(principalId);

        stream.AppendOne(new ContactRelationAdded(id, r.ToContactId, r.Kind, label, r.Since, note));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public async Task<OpResult<ContactDto>> RemoveRelationAsync(Guid principalId, Guid id, Guid toContactId, ContactRelationKind kind, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (!c.Relations.Any(x => x.ToContactId == toContactId && x.Kind == kind)) return OpResult<ContactDto>.NotFound();
        Stamp(principalId);

        stream.AppendOne(new ContactRelationRemoved(id, toContactId, kind));
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(updated!, ct));
    }

    /// <summary>Marks an outgoing edge as ended (relationship ran its course — distinct from removal, which erases a mistake).
    /// Re-adding the same target+kind revives it.</summary>
    public async Task<OpResult<ContactDto>> EndRelationAsync(Guid principalId, Guid id, Guid toContactId, ContactRelationKind kind, DateOnly? until, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        var edge = c.Relations.FirstOrDefault(x => x.ToContactId == toContactId && x.Kind == kind);
        if (edge is null) return OpResult<ContactDto>.NotFound();
        if (edge.Ended && edge.Until == until) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactRelationEnded(id, toContactId, kind, until));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    /// <summary>Resolved two-way view: outgoing edges (snapshot order) then incoming ones (by display name), each entry's
    /// Kind being the other contact's role relative to the viewed one (incoming = derived inverse). Ended edges are shown
    /// flagged. Edges whose other side is deleted, dangling, or outside the caller's readable books are filtered out.</summary>
    public async Task<OpResult<List<ContactRelationEntryDto>>> ListRelationsAsync(Guid principalId, Guid id, bool includeInferred = false, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is null || c.DeletedAt is not null) return OpResult<List<ContactRelationEntryDto>>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<List<ContactRelationEntryDto>>.Forbidden("No access to this contact.");

        var books = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var entries = new List<ContactRelationEntryDto>();

        var targets = (await session.LoadManyAsync<Contact>(ct, c.Relations.Select(r => r.ToContactId).Distinct().ToArray()))
            .Where(t => t.DeletedAt is null && books.Contains(t.AddressBookId)).ToDictionary(t => t.Id);
        foreach (var r in c.Relations)
            if (targets.TryGetValue(r.ToContactId, out var t))
                entries.Add(new ContactRelationEntryDto { ContactId = t.Id, DisplayName = t.DisplayName, Kind = r.Kind, Label = r.Label, Since = r.Since, Note = r.Note, Direction = ContactRelationDirection.Outgoing, Ended = r.Ended, Until = r.Until });

        var sources = await session.Query<Contact>()
            .Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == id)).ToListAsync(ct);
        foreach (var s in sources.Where(s => s.Id != id && books.Contains(s.AddressBookId)).OrderBy(s => s.SortName))
            foreach (var edge in s.Relations.Where(r => r.ToContactId == id))
                entries.Add(new ContactRelationEntryDto { ContactId = s.Id, DisplayName = s.DisplayName, Kind = edge.Kind.Inverse(), Label = null, Direction = ContactRelationDirection.Incoming, Ended = edge.Ended, Until = edge.Until });

        if (includeInferred)
        {
            // Kinship derives from the parent/child graph, which can span address books — resolve over all readable contacts.
            var all = (await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct))
                .Where(x => books.Contains(x.AddressBookId)).ToList();
            var byId = all.ToDictionary(x => x.Id);
            foreach (var kin in KinshipInference.Infer(id, all))
                if (byId.TryGetValue(kin.ContactId, out var k))
                    entries.Add(new ContactRelationEntryDto { ContactId = k.Id, DisplayName = k.DisplayName, Kind = kin.Kind, Label = null, Direction = ContactRelationDirection.Incoming, Provenance = RelationProvenance.Inferred });
        }

        return OpResult<List<ContactRelationEntryDto>>.Ok(entries);
    }

    // ---- Circles (computed on read, never stored) ----

    /// <summary>Social circles around a focus contact — the caller's own linked contact unless <paramref name="focusId"/> overrides.
    /// Members are limited to the caller's readable books.</summary>
    public async Task<OpResult<ContactCirclesDto>> CirclesAsync(Guid principalId, Guid? focusId, CancellationToken ct = default)
    {
        var focus = focusId ?? (await session.LoadAsync<Principal>(principalId, ct))?.ContactId;
        if (focus is not { } fid) return OpResult<ContactCirclesDto>.Invalid("No focus contact: pass focusId or link your contact via PUT /me/contact.");

        var c = await session.LoadAsync<Contact>(fid, ct);
        if (c is null || c.DeletedAt is not null) return OpResult<ContactCirclesDto>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactCirclesDto>.Forbidden("No access to this contact.");

        var books = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var all = (await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct))
            .Where(x => books.Contains(x.AddressBookId)).ToList();
        var organizations = await session.Query<ContactGroup>()
            .Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt == null).ToListAsync(ct);

        var byId = all.ToDictionary(x => x.Id);
        var memberships = CircleInference.Infer(fid, all, organizations);
        var circles = Enum.GetValues<CircleKind>().Select(kind => new ContactCircleDto
        {
            Kind = kind,
            Members = memberships.Where(m => m.Circle == kind)
                .Select(m => new CircleMemberDto { ContactId = m.ContactId, DisplayName = byId[m.ContactId].DisplayName, Kind = m.Kind, Degree = m.Degree, Provenance = m.Provenance })
                .OrderBy(m => m.Degree).ThenBy(m => byId[m.ContactId].SortName).ToList(),
        }).ToList();
        return OpResult<ContactCirclesDto>.Ok(new ContactCirclesDto { FocusContactId = fid, Circles = circles });
    }

    /// <summary>Links the caller's principal to its own contact ("this card is me") — the default circles focus.
    /// Plain document update; being a pointer to identity rather than contact content, it is not event-worthy.</summary>
    public async Task<OpResult> LinkSelfContactAsync(Guid principalId, Guid contactId, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(contactId, ct);
        if (c is null || c.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult.Forbidden("No access to this contact.");
        var principal = await session.LoadAsync<Principal>(principalId, ct);
        if (principal is null) return OpResult.NotFound();
        principal.ContactId = contactId;
        session.Store(principal);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    // Order-sensitive equality: order is part of the canonical content, so a reorder is a real change.
    private static bool RelationsEqual(IReadOnlyList<ContactRelation>? a, IReadOnlyList<ContactRelation> b) =>
        (a ?? []).Select(r => (r.ToContactId, r.Kind, r.Label, r.Since, r.Note, r.Ended, r.Until)).SequenceEqual(b.Select(r => (r.ToContactId, r.Kind, r.Label, r.Since, r.Note, r.Ended, r.Until)));

    private static bool ProfilesEqual(IReadOnlyList<ContactSocialProfile>? a, IReadOnlyList<ContactSocialProfile> b) =>
        (a ?? []).Select(p => (p.Service, p.Handle, p.Url, p.Preferred)).SequenceEqual(b.Select(p => (p.Service, p.Handle, p.Url, p.Preferred)));

    private static bool AddressesEqual(IReadOnlyList<ContactPostalAddress>? a, IReadOnlyList<ContactPostalAddress> b) =>
        (a ?? []).Select(x => (x.PlaceId, x.Type)).SequenceEqual(b.Select(x => (x.PlaceId, x.Type)));

    private static bool ChannelsEqual(IReadOnlyList<ContactReachChannel> a, IReadOnlyList<ContactReachChannel> b) =>
        a.SequenceEqual(b);   // records — structural equality, order-sensitive

    // ---- Sync write path (the DAV seam parses/serializes; this applies parsed state) ----

    public async Task<OpResult<DavWriteResult>> PutVcfAsync(
        Guid principalId, Guid addressBookId, string externalId, string rawVcard, string? ifMatch, bool ifNoneMatchStar, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult<DavWriteResult>.Forbidden("No write access to this address book.");

        var id = DeterministicGuid.From(externalId);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var existing = stream.Aggregate;
        // Streams are keyed by the resource uid alone, so a uid can resolve to a contact in another address book; applying
        // a PUT would move it. Refuse unless the caller can also write the book the contact currently lives in.
        if (existing is not null && existing.AddressBookId != addressBookId && !await access.CanWriteAddressBookAsync(principalId, existing.AddressBookId, ct))
            return OpResult<DavWriteResult>.Forbidden("This resource belongs to another collection.");
        var live = existing is { DeletedAt: null };
        if (ifNoneMatchStar && live) return OpResult<DavWriteResult>.Conflict("Resource already exists.");
        if (ifMatch is not null && (!live || existing!.ContentHash != ifMatch)) return OpResult<DavWriteResult>.Conflict("ETag mismatch.");
        Stamp(principalId);

        var p = VCardSerializer.ParseVCard(rawVcard);
        // Notes/pronouns are preserve-if-absent (most clients never emit them): set from the card when present, else keep existing.
        // DisplayNameFormat isn't a vCard field — always preserve the existing choice so a re-sync never resets it.
        var fields = new ContactFields(p.GivenName, null, p.FamilyName, null,
            p.Channels is null ? null : ReachChannelNormalizer.Normalize(p.Channels), p.Birthday, null,
            p.Notes ?? existing?.Notes, p.Pronouns ?? existing?.Pronouns, existing?.DisplayNameFormat ?? DisplayNameFormat.Full);
        // RELATED lines are authoritative wholesale-replace (a PUT without them clears relations + emergency designations).
        // Unresolvable target uuids are stored as-is — the target may sync in later or be unreadable to this caller; resolved reads filter.
        // Parent cycles are tolerated here (deliberately laxer import); inference is bounded, so they cannot hang it.
        var relations = (p.Relations ?? [])
            .Where(r => r.ToContactId != id)
            .DistinctBy(r => (r.ToContactId, r.Kind))
            .ToList();
        var emergency = (p.EmergencyContactIds ?? []).Where(x => x != id).Distinct().ToList();
        // Deceased and profiles are preserve-if-absent: most clients never emit the X-props, and a wholesale
        // interpretation would silently clear them on every sync. Consequence: this surface can set but never clear them.
        var deceased = p.Deceased ?? existing?.Deceased ?? false;
        var deathDate = p.Deceased is not null ? p.DeathDate : existing?.DeathDate;
        var profiles = p.Profiles is not null
            ? p.Profiles.Select(SocialProfileNormalizer.Normalize).Where(x => x.Service.Length > 0 && x.Handle.Length > 0).DistinctBy(x => (x.Service, Handle: x.Handle.ToLowerInvariant())).ToList()
            : existing?.Profiles ?? [];

        stream.AppendOne(new ContactImported(id, addressBookId, externalId, fields));   // also clears soft-delete
        if (!RelationsEqual(existing?.Relations, relations)) stream.AppendOne(new ContactRelationsReplaced(id, relations));
        if (!(existing?.EmergencyContactIds ?? []).SequenceEqual(emergency)) stream.AppendOne(new ContactEmergencyContactsReplaced(id, emergency));
        if (!ProfilesEqual(existing?.Profiles, profiles)) stream.AppendOne(new ContactProfilesReplaced(id, profiles));
        if (deceased && (existing is null || !existing.Deceased || existing.DeathDate != deathDate)) stream.AppendOne(new ContactMarkedDeceased(id, deathDate));
        await session.SaveChangesAsync(ct);
        // The hash is derived by the projection from the final state (incl. preserved values), so it matches a subsequent GET.
        var saved = await session.LoadAsync<Contact>(id, ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!live, saved!.ContentHash));
    }

    public async Task<OpResult> DeleteByUidAsync(Guid principalId, Guid addressBookId, string externalId, string? ifMatch, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult.Forbidden("No write access to this address book.");
        var id = DeterministicGuid.From(externalId);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        // c.AddressBookId != addressBookId guards the uid-collision case (the contact lives in another book).
        if (c is null || c.DeletedAt is not null || c.AddressBookId != addressBookId) return OpResult.NotFound();
        if (ifMatch is not null && c.ContentHash != ifMatch) return OpResult.Conflict("ETag mismatch.");
        Stamp(principalId);
        stream.AppendOne(new ContactDeleted(id));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private async Task<ContactDto> ToDtoAsync(Contact c, CancellationToken ct) =>
        c.ToResponse(await completeness.ScoreContactAsync(c, ct));

    // Stamp the acting principal + trace correlation onto every event appended in this unit of work (before SaveChangesAsync).
    private void Stamp(Guid principalId)
    {
        session.SetHeader(EventActor.HeaderKey, principalId.ToString());
        if (System.Diagnostics.Activity.Current?.TraceId is { } t) session.CorrelationId = t.ToString();
    }

    private static ContactFields FieldsOf(Contact c) =>
        new(c.GivenName, c.MiddleName, c.FamilyName, c.Nickname, c.Channels, c.Birthday, c.Tags, c.Notes, c.Pronouns, c.DisplayNameFormat);
}
