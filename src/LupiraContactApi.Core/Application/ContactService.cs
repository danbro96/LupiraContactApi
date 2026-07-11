using LupiraContactApi.Auth;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using LupiraContactApi.Mappers;
using LupiraContactApi.Serialization;
using Marten;
using JasperFx.Events;

namespace LupiraContactApi.Application;

/// <summary>Contact core shared by REST, CardDAV, and MCP. Event-sourced like <see cref="CalendarItemService"/>; a contact belongs to one address book.</summary>
public sealed class ContactService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness)
{
    public async Task<OpResult<ContactDto>> CreateAsync(Guid principalId, CreateContactRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, r.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this address book.");

        var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var fields = new ContactFields(r.NamePrefix, r.GivenName, r.MiddleName, r.FamilyName, r.NameSuffix, r.Nickname, r.Emails, r.Phones, r.Birthday, r.Tags);
        var hash = ContentHash.Of(ContactContent.Canonical(uid, fields, [], [], [], false, null));

        session.Events.StartStream<Contact>(id, new ContactCreated(id, r.AddressBookId, uid, fields, hash));
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
            contacts = contacts.Where(c => c.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var ordered = contacts.OrderBy(c => c.DisplayName).ToList();
        var scores = await completeness.ScoreContactsAsync(ordered, ct);
        return OpResult<List<ContactDto>>.Ok([.. ordered.Select(c => c.ToResponse(scores[c.Id]))]);
    }

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

        var merged = new ContactFields(
            r.NamePrefix ?? c.NamePrefix,
            r.GivenName ?? c.GivenName,
            r.MiddleName ?? c.MiddleName,
            r.FamilyName ?? c.FamilyName,
            r.NameSuffix ?? c.NameSuffix,
            r.Nickname ?? c.Nickname,
            MergeDistinct(c.Emails, r.Emails),
            MergeDistinct(c.Phones, r.Phones),
            r.Birthday ?? c.Birthday,
            MergeDistinct(c.Tags, r.Tags));
        var hash = HashOf(c, fields: merged);

        stream.AppendOne(new ContactRevised(id, merged, hash));
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

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult.Forbidden("No write access to this contact.");
        stream.AppendOne(new ContactDeleted(id, DateTimeOffset.UtcNow));
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

        stream.AppendOne(new ContactMarkedDeceased(id, deathDate, HashOf(c, deceased: true, deathDate: deathDate)));
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

        stream.AppendOne(new ContactDeceasedCleared(id, HashOf(c, deceased: false, deathDate: null)));
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

        stream.AppendOne(new ContactProfilesReplaced(id, next, HashOf(c, profiles: next)));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public async Task<OpResult<ContactDto>> SetAddressesAsync(Guid principalId, Guid id, IReadOnlyList<ContactPostalAddress> addresses, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = addresses.Select(a => new ContactPostalAddress { PlaceId = a.PlaceId, FormattedAddress = string.IsNullOrWhiteSpace(a.FormattedAddress) ? null : a.FormattedAddress.Trim(), Type = a.Type }).ToList();
        if (next.Any(a => a.PlaceId is null && a.FormattedAddress is null)) return OpResult<ContactDto>.Invalid("Each address needs a place id or a formatted address.");
        if (AddressesEqual(c.Addresses, next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));

        stream.AppendOne(new ContactAddressesReplaced(id, next));   // addresses are outside the canonical content — no hash, ETag unchanged
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

        stream.AppendOne(new ContactEmergencyContactsReplaced(id, next, HashOf(c, emergencyIds: next)));
        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // ---- Relations (directed edges on the from-contact's stream; see ContactRelation) ----

    /// <summary>Upserts "<c>r.ToContactId</c> is this contact's <c>r.Kind</c>" (re-adding the same key revises the label and revives an ended edge).
    /// Requires write on this contact's book and read on the target's; the import path is deliberately laxer (no target check).
    /// Parent/child adds are refused when they would make someone their own ancestor.
    /// Kinship invariant: siblinghood is expressed as shared parentage, so a <c>Sibling</c> add between contacts where either
    /// side already has a parent instead assigns that parent to the other (no explicit edge stored); and adding a
    /// <c>Parent</c>/<c>Child</c> dissolves the newly-parented contact's explicit sibling edges into shared parentage.</summary>
    public async Task<OpResult<ContactDto>> AddRelationAsync(Guid principalId, Guid id, AddContactRelationRequest r, CancellationToken ct = default)
    {
        var writer = new RelationWriter(session);
        var c = await writer.LoadAsync(id, ct);
        if (c is null) return OpResult<ContactDto>.NotFound();
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

        // Sibling where a parent is already recorded on either side → express it as shared parentage instead of an edge.
        if (r.Kind == ContactRelationKind.Sibling)
        {
            var fromParents = await ParentIdsAsync(id, c.Relations, ct);
            var toParents = await ParentIdsAsync(r.ToContactId, target.Relations, ct);
            if (fromParents.Count > 0 || toParents.Count > 0)
            {
                if (fromParents.Count > 0 && !await access.CanWriteAddressBookAsync(principalId, target.AddressBookId, ct))
                    return OpResult<ContactDto>.Forbidden("No write access to the related contact to assign its parent.");
                foreach (var p in fromParents) await writer.AddParentAsync(r.ToContactId, p, ct);
                foreach (var p in toParents) await writer.AddParentAsync(id, p, ct);
                await session.SaveChangesAsync(ct);
                return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
            }
        }

        if (c.Relations.Any(x => x.ToContactId == r.ToContactId && x.Kind == r.Kind && x.Label == label && !x.Ended))
            return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // identical live edge: no event, no ETag churn

        await writer.UpsertAsync(id, r.ToContactId, r.Kind, label, ct);

        // Gaining a parent dissolves the newly-parented contact's explicit sibling edges into shared parentage.
        if (r.Kind == ContactRelationKind.Parent)
            await DissolveSiblingsAsync(writer, principalId, id, await ParentIdsAsync(id, writer.WorkingRelations(id), ct), ct);
        else if (r.Kind == ContactRelationKind.Child)
        {
            var childParents = await ParentIdsAsync(r.ToContactId, target.Relations, ct);
            childParents.Add(id);
            await DissolveSiblingsAsync(writer, principalId, r.ToContactId, childParents, ct);
        }

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

        var next = c.Relations.Where(x => x.ToContactId != toContactId || x.Kind != kind).ToList();
        stream.AppendOne(new ContactRelationRemoved(id, toContactId, kind, HashOf(c, relations: next)));
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

        var next = c.Relations.Select(x => x == edge ? new ContactRelation { ToContactId = x.ToContactId, Kind = x.Kind, Label = x.Label, Ended = true, Until = until } : x).ToList();
        stream.AppendOne(new ContactRelationEnded(id, toContactId, kind, until, HashOf(c, relations: next)));
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
                entries.Add(new ContactRelationEntryDto { ContactId = t.Id, DisplayName = t.DisplayName, Kind = r.Kind.AsKinship(), Label = r.Label, Direction = ContactRelationDirection.Outgoing, Ended = r.Ended, Until = r.Until });

        var sources = await session.Query<Contact>()
            .Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == id)).ToListAsync(ct);
        foreach (var s in sources.Where(s => s.Id != id && books.Contains(s.AddressBookId)).OrderBy(s => s.DisplayName))
            foreach (var edge in s.Relations.Where(r => r.ToContactId == id))
                entries.Add(new ContactRelationEntryDto { ContactId = s.Id, DisplayName = s.DisplayName, Kind = edge.Kind.Inverse().AsKinship(), Label = null, Direction = ContactRelationDirection.Incoming, Ended = edge.Ended, Until = edge.Until });

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
                .OrderBy(m => m.Degree).ThenBy(m => m.DisplayName).ToList(),
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
        (a ?? []).Select(r => (r.ToContactId, r.Kind, r.Label, r.Ended, r.Until)).SequenceEqual(b.Select(r => (r.ToContactId, r.Kind, r.Label, r.Ended, r.Until)));

    private static bool ProfilesEqual(IReadOnlyList<ContactSocialProfile>? a, IReadOnlyList<ContactSocialProfile> b) =>
        (a ?? []).Select(p => (p.Service, p.Handle, p.Url, p.Preferred)).SequenceEqual(b.Select(p => (p.Service, p.Handle, p.Url, p.Preferred)));

    private static bool AddressesEqual(IReadOnlyList<ContactPostalAddress>? a, IReadOnlyList<ContactPostalAddress> b) =>
        (a ?? []).Select(x => (x.PlaceId, x.FormattedAddress, x.Type)).SequenceEqual(b.Select(x => (x.PlaceId, x.FormattedAddress, x.Type)));

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

        var p = VCardSerializer.ParseVCard(rawVcard);
        var fields = new ContactFields(null, p.GivenName, null, p.FamilyName, null, null, p.Emails, p.Phones, p.Birthday, null);
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

        // The hash covers the final state including preserved values, so the returned ETag matches a subsequent GET.
        var hash = ContentHash.Of(ContactContent.Canonical(externalId, fields, relations, emergency, profiles, deceased, deathDate));
        stream.AppendOne(new ContactImported(id, addressBookId, externalId, fields, hash));   // also clears soft-delete
        if (!RelationsEqual(existing?.Relations, relations)) stream.AppendOne(new ContactRelationsReplaced(id, relations));
        if (!(existing?.EmergencyContactIds ?? []).SequenceEqual(emergency)) stream.AppendOne(new ContactEmergencyContactsReplaced(id, emergency, hash));
        if (!ProfilesEqual(existing?.Profiles, profiles)) stream.AppendOne(new ContactProfilesReplaced(id, profiles, hash));
        if (deceased && (existing is null || !existing.Deceased || existing.DeathDate != deathDate)) stream.AppendOne(new ContactMarkedDeceased(id, deathDate, hash));
        await session.SaveChangesAsync(ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!live, hash));
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
        stream.AppendOne(new ContactDeleted(id, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    private async Task<ContactDto> ToDtoAsync(Contact c, CancellationToken ct) =>
        c.ToResponse(await completeness.ScoreContactAsync(c, ct));

    /// <summary>Content hash of a contact's snapshot with any dimension overridden by the prospective value.</summary>
    private static string HashOf(Contact c, ContactFields? fields = null, IReadOnlyList<ContactRelation>? relations = null,
        IReadOnlyList<Guid>? emergencyIds = null, IReadOnlyList<ContactSocialProfile>? profiles = null,
        bool? deceased = null, DateOnly? deathDate = null) =>
        ContentHash.Of(ContactContent.Canonical(c.ExternalId, fields ?? FieldsOf(c), relations ?? c.Relations,
            emergencyIds ?? c.EmergencyContactIds, profiles ?? c.Profiles, deceased ?? c.Deceased, deceased is null ? c.DeathDate : deathDate));

    private static ContactFields FieldsOf(Contact c) =>
        new(c.NamePrefix, c.GivenName, c.MiddleName, c.FamilyName, c.NameSuffix, c.Nickname, c.Emails, c.Phones, c.Birthday, c.Tags);

    // ---- Kinship invariant + sweep ----

    /// <summary>One-time (idempotent) cleanup that converts every explicit Sibling edge whose endpoints have a recorded
    /// parent into shared parentage, so siblinghood is uniformly derived. Scoped to the caller's writable books.</summary>
    public async Task<OpResult<int>> NormalizeSiblingsAsync(Guid principalId, Guid? addressBookId, CancellationToken ct = default)
    {
        var books = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        if (addressBookId is { } abid)
        {
            if (!books.Contains(abid)) return OpResult<int>.Forbidden("No access to this address book.");
            books = [abid];
        }

        var total = 0;
        // Fixed point: a pass may parent a contact whose own siblings only convert on the next pass; converges since each
        // conversion strictly removes a (parent ∧ sibling-edge) violation. The pass count is bounded by the contact count.
        for (var pass = 0; ; pass++)
        {
            var all = (await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct))
                .Where(x => books.Contains(x.AddressBookId)).ToList();
            var byId = all.ToDictionary(x => x.Id);
            var writer = new RelationWriter(session);
            var converted = 0;

            foreach (var c in all)
            {
                var (parents, siblings) = KinshipInference.Normalize(c.Id, all);
                if (parents.Count == 0 || siblings.Count == 0) continue;
                foreach (var sib in siblings)
                {
                    if (!byId.TryGetValue(sib, out var sc) || !await access.CanWriteAddressBookAsync(principalId, sc.AddressBookId, ct)) continue;
                    foreach (var p in parents) await writer.AddParentAsync(sib, p, ct);
                    if (c.Relations.Any(r => r.ToContactId == sib && r.Kind == ContactRelationKind.Sibling)) await writer.RemoveSiblingAsync(c.Id, sib, ct);
                    if (sc.Relations.Any(r => r.ToContactId == c.Id && r.Kind == ContactRelationKind.Sibling)) await writer.RemoveSiblingAsync(sib, c.Id, ct);
                    converted++;
                }
            }

            await session.SaveChangesAsync(ct);
            total += converted;
            if (converted == 0 || pass >= all.Count) break;
        }
        return OpResult<int>.Ok(total);
    }

    // Parents of a contact = its outgoing Parent edges (from the supplied relation list) ∪ contacts holding a Child edge to it. Ended edges assert nothing.
    private async Task<HashSet<Guid>> ParentIdsAsync(Guid id, IReadOnlyList<ContactRelation> relations, CancellationToken ct)
    {
        var parents = relations.Where(r => r.Kind == ContactRelationKind.Parent && !r.Ended).Select(r => r.ToContactId).ToHashSet();
        var incoming = await session.Query<Contact>().Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == id)).ToListAsync(ct);
        foreach (var x in incoming)
            if (x.Id != id && x.Relations.Any(r => r.ToContactId == id && r.Kind == ContactRelationKind.Child && !r.Ended)) parents.Add(x.Id);
        return parents;
    }

    // Give each explicit sibling of the newly-parented contact (in a writable book) that contact's parents, then drop the
    // Sibling edge on whichever side stored it. childParents already reflects the just-added parent.
    private async Task DissolveSiblingsAsync(RelationWriter writer, Guid principalId, Guid childId, IReadOnlyCollection<Guid> childParents, CancellationToken ct)
    {
        if (childParents.Count == 0) return;
        if (await writer.LoadAsync(childId, ct) is null) return;

        var outgoing = writer.WorkingRelations(childId).Where(r => r.Kind == ContactRelationKind.Sibling).Select(r => r.ToContactId).ToHashSet();
        var incoming = await session.Query<Contact>().Where(x => x.DeletedAt == null && x.Relations.Any(r => r.ToContactId == childId)).ToListAsync(ct);
        var incomingSibs = incoming.Where(x => x.Relations.Any(r => r.ToContactId == childId && r.Kind == ContactRelationKind.Sibling)).ToDictionary(x => x.Id);

        foreach (var sib in outgoing.Union(incomingSibs.Keys).ToList())
        {
            var sc = incomingSibs.TryGetValue(sib, out var found) ? found : await session.LoadAsync<Contact>(sib, ct);
            if (sc is null || sc.DeletedAt is not null || !await access.CanWriteAddressBookAsync(principalId, sc.AddressBookId, ct)) continue;
            foreach (var p in childParents) await writer.AddParentAsync(sib, p, ct);
            if (outgoing.Contains(sib)) await writer.RemoveSiblingAsync(childId, sib, ct);
            if (incomingSibs.ContainsKey(sib)) await writer.RemoveSiblingAsync(sib, childId, ct);
        }
    }

    /// <summary>Batches relation-edge writes across several contact streams in one session, tracking each contact's evolving
    /// edge list so multiple appends to the same contact carry correct incremental content hashes.</summary>
    private sealed class RelationWriter(IDocumentSession session)
    {
        private sealed record Entry(IEventStream<Contact> Stream, Contact Contact, List<ContactRelation> Relations);
        private readonly Dictionary<Guid, Entry?> _entries = new();
        private IReadOnlyCollection<Contact>? _live;

        /// <summary>Current (pre-append) aggregate, or null if missing/deleted; caches the writable stream + working edge list.</summary>
        public async Task<Contact?> LoadAsync(Guid id, CancellationToken ct) => (await GetAsync(id, ct))?.Contact;

        public IReadOnlyList<ContactRelation> WorkingRelations(Guid id) =>
            _entries.TryGetValue(id, out var e) && e is not null ? e.Relations : [];

        public async Task UpsertAsync(Guid id, Guid toId, ContactRelationKind kind, string? label, CancellationToken ct)
        {
            if (await GetAsync(id, ct) is not { } e) return;
            e.Relations.RemoveAll(r => r.ToContactId == toId && r.Kind == kind);
            e.Relations.Add(new ContactRelation { ToContactId = toId, Kind = kind, Label = label });
            e.Stream.AppendOne(new ContactRelationAdded(id, toId, kind, label, HashOf(e)));
        }

        public async Task AddParentAsync(Guid childId, Guid parentId, CancellationToken ct)
        {
            if (await GetAsync(childId, ct) is not { } e) return;
            if (e.Relations.Any(r => r.ToContactId == parentId && r.Kind == ContactRelationKind.Parent && !r.Ended)) return;
            // Invariant repair must not corrupt: silently skip an assignment that would make someone their own ancestor.
            _live ??= await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct);
            if (KinshipInference.WouldCreateParentCycle(childId, parentId, _live)) return;
            e.Relations.RemoveAll(r => r.ToContactId == parentId && r.Kind == ContactRelationKind.Parent);
            e.Relations.Add(new ContactRelation { ToContactId = parentId, Kind = ContactRelationKind.Parent, Label = null });
            e.Stream.AppendOne(new ContactRelationAdded(childId, parentId, ContactRelationKind.Parent, null, HashOf(e)));
        }

        public async Task RemoveSiblingAsync(Guid id, Guid toId, CancellationToken ct)
        {
            if (await GetAsync(id, ct) is not { } e) return;
            if (e.Relations.RemoveAll(r => r.ToContactId == toId && r.Kind == ContactRelationKind.Sibling) == 0) return;
            e.Stream.AppendOne(new ContactRelationRemoved(id, toId, ContactRelationKind.Sibling, HashOf(e)));
        }

        private async Task<Entry?> GetAsync(Guid id, CancellationToken ct)
        {
            if (_entries.TryGetValue(id, out var cached)) return cached;
            var stream = await session.Events.FetchForWriting<Contact>(id, ct);
            var c = stream.Aggregate;
            var entry = c is null || c.DeletedAt is not null ? null : new Entry(stream, c, [.. c.Relations]);
            _entries[id] = entry;
            return entry;
        }

        // The batch only touches relations, so the snapshot's other dimensions are current.
        private static string HashOf(Entry e) => ContentHash.Of(ContactContent.Canonical(
            e.Contact.ExternalId, FieldsOf(e.Contact), e.Relations, e.Contact.EmergencyContactIds, e.Contact.Profiles, e.Contact.Deceased, e.Contact.DeathDate));
    }
}
