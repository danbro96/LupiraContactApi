using System.Text.Json.Nodes;
using JasperFx;
using JasperFx.Events;
using LupiraContactApi.Core.Application.Results;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.AddressBooks;
using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Domain.Inference;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Mappers;
using LupiraContactApi.Core.Serialization;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Contact core shared by REST, the legacy sync gateway, and MCP. Event-sourced; a contact belongs to one
/// address book. Mutations may carry an <c>Idempotency-Key</c> command id (see <see cref="Idempotency"/>) and an
/// <c>occurredAt</c> client stamp (see <see cref="SectionLww"/>); creates dedup on <c>SourceKey</c> instead.</summary>
public sealed class ContactService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness, Idempotency idempotency, DavCards cards)
{
    /// <summary>Commit staged events + the dedup ledger row in one transaction. False when the dedup race was
    /// lost — the caller re-reads and returns the already-committed state (idempotent success).</summary>
    private async Task<bool> SaveGuardedAsync(Guid? commandId, Guid aggregateId, int resultVersion, CancellationToken ct)
    {
        idempotency.Record(commandId, aggregateId, resultVersion);
        try
        {
            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Idempotency.IsDuplicate(ex))
        {
            return false;
        }

        return true;
    }

    /// <summary>Response for a mutation whose command id is already in the ledger: the current state, deleted or
    /// not — the original call succeeded, so the replay must too.</summary>
    private async Task<OpResult<ContactDto>> ReplayedAsync(Guid id, CancellationToken ct) =>
        await session.LoadAsync<Contact>(id, ct) is { } current
            ? OpResult<ContactDto>.Ok(await ToDtoAsync(current, ct))
            : OpResult<ContactDto>.NotFound();

    public async Task<OpResult<ContactDto>> CreateAsync(Guid principalId, CreateContactRequest r, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, r.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this address book.");

        var channels = ReachChannelNormalizer.Normalize(r.Channels ?? []);
        if (ReachChannelNormalizer.HasPreferredConflict(channels)) return OpResult<ContactDto>.Invalid("At most one preferred channel per medium.");

        // A client SourceKey pins the stream id (offline replay-safe create); else a random uid.
        var hasKey = !string.IsNullOrWhiteSpace(r.SourceKey);
        var uid = hasKey ? r.SourceKey!.Trim() : $"{Guid.NewGuid():N}@cal.lupira.com";
        var id = DeterministicGuid.From(uid);
        var stream = hasKey ? await session.Events.FetchForWriting<Contact>(id, ct) : null;
        if (stream?.Aggregate is { DeletedAt: null } existing)
            return OpResult<ContactDto>.Ok(await ToDtoAsync(existing, ct));   // idempotent hit — no new events
        Stamp(principalId);

        var fields = new ContactFields(r.GivenName, r.MiddleName, r.FamilyName, r.Nickname, channels, r.Birthday, r.Tags, r.Notes, r.Pronouns, r.DisplayNameFormat ?? DisplayNameFormat.FirstLast, r.Kind ?? ContactKind.Individual);

        var created = new ContactCreated(id, r.AddressBookId, uid, fields);
        if (stream is not null) stream.AppendOne(created);   // keyed create over a soft-deleted stream resurrects it
        else session.Events.StartStream<Contact>(id, created);
        await session.SaveChangesAsync(ct);
        var c = await session.LoadAsync<Contact>(id, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync(c!, ct));
    }

    public async Task<OpResult<List<ContactDto>>> QueryAsync(Guid principalId, string? query, Guid? addressBookId, CancellationToken ct = default)
    {
        if (await ReadableLiveContactsAsync(principalId, addressBookId, ct) is not { } readable)
            return OpResult<List<ContactDto>>.Forbidden("No access to this address book.");

        IEnumerable<Contact> contacts = await readable.ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(query)) contacts = ContactNameMatcher.Search(contacts, query);

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
        {
            if (!await access.CanWriteAddressBookAsync(principalId, abid, ct))
                return OpResult<List<ContactDto>>.Forbidden($"No write access to address book {abid}.");
        }

        Stamp(principalId);
        var ids = new List<Guid>(requests.Count);
        foreach (var r in requests)
        {
            var channels = ReachChannelNormalizer.Normalize(r.Channels ?? []);
            if (ReachChannelNormalizer.HasPreferredConflict(channels))
                return OpResult<List<ContactDto>>.Invalid($"Contact '{r.GivenName} {r.FamilyName}': at most one preferred channel per medium.");
            var uid = $"{Guid.NewGuid():N}@cal.lupira.com";
            var id = DeterministicGuid.From(uid);
            var fields = new ContactFields(r.GivenName, r.MiddleName, r.FamilyName, r.Nickname, channels, r.Birthday, r.Tags, r.Notes, r.Pronouns, r.DisplayNameFormat ?? DisplayNameFormat.FirstLast, r.Kind ?? ContactKind.Individual);
            session.Events.StartStream<Contact>(id, new ContactCreated(id, r.AddressBookId, uid, fields));
            ids.Add(id);
        }

        await session.SaveChangesAsync(ct);

        var loaded = (await session.Query<Contact>().Where(c => ids.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
        var ordered = ids.Select(i => loaded[i]).ToList();
        var scores = await completeness.ScoreContactsAsync(ordered, ct);
        return OpResult<List<ContactDto>>.Ok([.. ordered.Select(c => c.ToResponse(scores[c.Id]))]);
    }

    /// <summary>Batch-match input names to accessible contacts for import disambiguation, per
    /// <see cref="ContactNameMatcher.Resolve"/>. Not phonetic. Candidates are capped.</summary>
    public async Task<OpResult<List<ContactNameMatch>>> ResolveByNameAsync(Guid principalId, IReadOnlyList<string> names, Guid? addressBookId, CancellationToken ct = default)
    {
        if (names.Count == 0) return OpResult<List<ContactNameMatch>>.Invalid("At least one name is required.");
        if (names.Count > MaxBatch) return OpResult<List<ContactNameMatch>>.Invalid($"At most {MaxBatch} names per batch.");

        if (await ReadableLiveContactsAsync(principalId, addressBookId, ct) is not { } readable)
            return OpResult<List<ContactNameMatch>>.Forbidden("No access to this address book.");

        var pool = await readable.ToListAsync(ct);
        return OpResult<List<ContactNameMatch>>.Ok(ContactNameMatcher.Resolve(names, pool));
    }

    /// <summary>Id → display name for the requested contacts the caller can read. Unknown, deleted and
    /// inaccessible ids are omitted — the existence check sibling services (cal-api attendees) run.</summary>
    public async Task<OpResult<List<ContactRef>>> LookupAsync(Guid principalId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return OpResult<List<ContactRef>>.Invalid("At least one contact id is required.");
        if (ids.Count > MaxBatch) return OpResult<List<ContactRef>>.Invalid($"At most {MaxBatch} ids per request.");

        var wanted = ids.Distinct().ToList();
        var found = await (await ReadableLiveContactsAsync(principalId, ct)).Where(c => wanted.Contains(c.Id)).ToListAsync(ct);
        return OpResult<List<ContactRef>>.Ok([.. found
            .OrderBy(c => c.SortName)
            .Select(c => new ContactRef { ContactId = c.Id, DisplayName = c.DisplayName })]);
    }

    /// <summary>Live, non-deceased contacts with a birthday across the caller's readable address books — the source
    /// of cal-api's synthesized Birthdays calendar. Year-less birthdays recur on month-day only.</summary>
    public async Task<OpResult<List<ContactBirthdayDto>>> BirthdaysAsync(Guid principalId, CancellationToken ct = default)
    {
        var withBirthday = await (await ReadableLiveContactsAsync(principalId, ct))
            .Where(c => !c.Deceased && c.Birthday != null).ToListAsync(ct);
        return OpResult<List<ContactBirthdayDto>>.Ok([.. withBirthday
            .OrderBy(c => c.Birthday!.Month).ThenBy(c => c.Birthday!.Day).ThenBy(c => c.SortName)
            .Select(c => new ContactBirthdayDto
            {
                ContactId = c.Id,
                DisplayName = c.DisplayName,
                Year = c.Birthday!.Year,
                Month = c.Birthday.Month,
                Day = c.Birthday.Day,
            })]);
    }

    private async Task<IQueryable<Contact>> ReadableLiveContactsAsync(Guid principalId, CancellationToken ct) =>
        LiveContactsIn(await access.AccessibleAddressBookIdsAsync(principalId, ct));

    // Null when addressBookId is given but not readable by the principal.
    private async Task<IQueryable<Contact>?> ReadableLiveContactsAsync(Guid principalId, Guid? addressBookId, CancellationToken ct) =>
        addressBookId is not { } abid ? await ReadableLiveContactsAsync(principalId, ct)
        : await access.CanReadAddressBookAsync(principalId, abid, ct) ? LiveContactsIn([abid])
        : null;

    private IQueryable<Contact> LiveContactsIn(List<Guid> bookIds) =>
        session.Query<Contact>().Where(c => c.DeletedAt == null && bookIds.Contains(c.AddressBookId));

    public async Task<OpResult<ContactDto>> GetAsync(Guid principalId, Guid id, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No access to this contact.");
        return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
    }

    /// <summary>Merge-update an existing contact: provided scalars overwrite, provided email/phone/tag arrays
    /// union onto the existing values (deduped), null fields are kept. Never wipes unmentioned fields.</summary>
    public async Task<OpResult<ContactDto>> ReviseAsync(Guid principalId, Guid id, ReviseContactRequest r, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
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
            r.DisplayNameFormat ?? c.DisplayNameFormat,
            r.Kind ?? c.Kind);

        stream.AppendOne(new ContactRevised(id, merged, r.OccurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
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

    public async Task<OpResult<ContactDto>> SetChannelsAsync(Guid principalId, Guid id, IReadOnlyList<ContactReachChannel> channels, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = ReachChannelNormalizer.Normalize(channels);
        if (ReachChannelNormalizer.HasPreferredConflict(next)) return OpResult<ContactDto>.Invalid("At most one preferred channel per medium.");
        if (ChannelsEqual(c.Channels, next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactRevised(id, c.Fields() with { Channels = next }, occurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public Task<OpResult<ContactDto>> SetTagsAsync(Guid principalId, Guid id, string[] tags, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default) =>
        ReplaceMultiAsync(principalId, id, tags, c => c.Tags, (c, next) => c.Fields() with { Tags = next }, occurredAt, commandId, ct);

    private async Task<OpResult<ContactDto>> ReplaceMultiAsync(Guid principalId, Guid id, string[] incoming,
        Func<Contact, string[]?> current, Func<Contact, string[], ContactFields> apply, DateTimeOffset? occurredAt, Guid? commandId, CancellationToken ct)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = NormalizeMulti(incoming);
        if (NormalizeMulti(current(c)).SequenceEqual(next, StringComparer.Ordinal)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // no event, no ETag churn
        Stamp(principalId);

        var merged = apply(c, next);
        stream.AppendOne(new ContactRevised(id, merged, occurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // Trim, drop blanks, de-duplicate case-insensitively (first casing wins). Order is preserved and significant.
    private static string[] NormalizeMulti(string[]? values) =>
        [.. (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    public async Task<OpResult> DeleteAsync(Guid principalId, Guid id, Guid? commandId = null, CancellationToken ct = default)
    {
        // A replayed delete after a lost response finds the contact already gone — 404 would wedge an offline
        // client's outbox, so the ledger turns it into an idempotent success.
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult.Ok();
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult.Forbidden("No write access to this contact.");
        // Refused before the ledger row is staged, so an outbox replay of the same key is refused again, not passed.
        if (await SelfContactRefusalAsync(c, ct) is { } refusal) return OpResult.Conflict(refusal);
        Stamp(principalId);
        stream.AppendOne(new ContactDeleted(id));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult.Ok();
    }

    // A principal's own contact is its identity elsewhere (attendee, circles focus); deleting it would strand those links.
    private async Task<string?> SelfContactRefusalAsync(Contact c, CancellationToken ct) =>
        await session.Query<Principal>().AnyAsync(p => p.ContactId == c.Id, ct)
            ? $"This is {c.DisplayName}'s own contact — it can't be deleted."
            : null;

    // ---- Moves between address books (the id, and so every relation, membership and link to it, survives) ----

    public const int MaxMoveBatch = 500;

    /// <summary>Moves a contact to another address book. Needs write access to both; moving it to its current book is a no-op.</summary>
    public async Task<OpResult<ContactDto>> MoveAsync(Guid principalId, Guid id, Guid addressBookId, CancellationToken ct = default)
    {
        if (await DenyMoveTargetAsync(principalId, addressBookId, ct) is { } denied) return new(denied.Status, null, denied.Error);
        switch ((await StageMovesAsync(principalId, [id], addressBookId, null, ct))[0].Outcome)
        {
            case ContactMoveOutcome.NotFound: return OpResult<ContactDto>.NotFound();
            case ContactMoveOutcome.Forbidden: return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        }

        await session.SaveChangesAsync(ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    /// <summary>Moves many contacts in one transaction, reporting each id's outcome in input order (duplicates collapse).
    /// A missing or unwritable target fails the whole call.</summary>
    public async Task<OpResult<List<ContactMoveResult>>> MoveManyAsync(Guid principalId, IReadOnlyCollection<Guid> ids, Guid addressBookId, CancellationToken ct = default)
    {
        if (ids.Count == 0) return OpResult<List<ContactMoveResult>>.Invalid("At least one contact id is required.");
        if (ids.Count > MaxMoveBatch) return OpResult<List<ContactMoveResult>>.Invalid($"At most {MaxMoveBatch} contacts per move.");
        if (await DenyMoveTargetAsync(principalId, addressBookId, ct) is { } denied) return new(denied.Status, null, denied.Error);

        var results = await StageMovesAsync(principalId, ids, addressBookId, null, ct);
        await session.SaveChangesAsync(ct);
        return OpResult<List<ContactMoveResult>>.Ok(results);
    }

    internal async Task<OpResult?> DenyMoveTargetAsync(Guid principalId, Guid addressBookId, CancellationToken ct)
    {
        if (addressBookId == Guid.Empty) return OpResult.Invalid("addressBookId is required.");
        if (await session.LoadAsync<AddressBook>(addressBookId, ct) is null) return OpResult.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult.Forbidden("No write access to the target address book.");
        return null;
    }

    /// <summary>Appends a move for each live contact the caller can write, leaving the save to the caller so a group move can
    /// commit its members with it. With <paramref name="onlyFromBookId"/>, contacts living elsewhere are skipped. The target
    /// must already have passed <see cref="DenyMoveTargetAsync"/>.</summary>
    internal async Task<List<ContactMoveResult>> StageMovesAsync(Guid principalId, IEnumerable<Guid> ids, Guid addressBookId, Guid? onlyFromBookId, CancellationToken ct)
    {
        Stamp(principalId);
        var writable = new Dictionary<Guid, bool> { [addressBookId] = true };
        var results = new List<ContactMoveResult>();
        foreach (var id in ids.Distinct())
        {
            var stream = await session.Events.FetchForWriting<Contact>(id, ct);
            var outcome = stream.Aggregate switch
            {
                not { DeletedAt: null } => ContactMoveOutcome.NotFound,
                var c when c.AddressBookId == addressBookId => ContactMoveOutcome.Unchanged,
                var c when onlyFromBookId is { } from && c.AddressBookId != from => ContactMoveOutcome.Skipped,
                var c => await CanWriteAsync(c.AddressBookId) ? ContactMoveOutcome.Moved : ContactMoveOutcome.Forbidden,
            };
            if (outcome == ContactMoveOutcome.Moved) stream.AppendOne(new ContactMoved(id, addressBookId));
            results.Add(new ContactMoveResult { ContactId = id, Outcome = outcome });
        }

        return results;

        async Task<bool> CanWriteAsync(Guid bookId) =>
            writable.TryGetValue(bookId, out var known) ? known : writable[bookId] = await access.CanWriteAddressBookAsync(principalId, bookId, ct);
    }

    // ---- Deceased (death is not deletion — the contact stays in the kinship graph) ----

    public async Task<OpResult<ContactDto>> SetDeceasedAsync(Guid principalId, Guid id, DateOnly? deathDate, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (c.Deceased && c.DeathDate == deathDate) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));   // no event, no ETag churn
        Stamp(principalId);

        stream.AppendOne(new ContactMarkedDeceased(id, deathDate, occurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public async Task<OpResult<ContactDto>> ClearDeceasedAsync(Guid principalId, Guid id, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        if (!c.Deceased) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactDeceasedCleared(id, occurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    // ---- Avatar (a pointer to an image, never bytes — outside the canonical content, like addresses) ----

    public async Task<OpResult<ContactDto>> SetAvatarAsync(Guid principalId, Guid id, string? avatarRef, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = string.IsNullOrWhiteSpace(avatarRef) ? null : avatarRef.Trim();
        if (c.AvatarRef == next) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactAvatarSet(id, next, occurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    /// <summary>Shallow-merge a JSON object into the contact's annotation metadata (top-level keys overwrite).
    /// The channel for completeness N/A acknowledgments: <c>{"completeness":{"na":["organisation"]}}</c>.</summary>
    public async Task<OpResult<ContactDto>> AttachMetadataAsync(Guid principalId, Guid id, JsonNode patch, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");
        Stamp(principalId);

        var current = (JsonNode.Parse(string.IsNullOrWhiteSpace(c.Metadata) ? "{}" : c.Metadata) as JsonObject) ?? new JsonObject();
        if (patch is JsonObject obj)
            foreach (var kv in obj) current[kv.Key] = kv.Value?.DeepClone();
        stream.AppendOne(new ContactMetadataAttached(id, current.ToJsonString(), occurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    /// <summary>Check-in worklist: contacts ranked thinnest-first (score asc, then sort name). Scores are
    /// kind-aware (person vs organisation card) and N/A-acknowledged fields are already excluded.</summary>
    public async Task<OpResult<List<ContactDto>>> ThinContactsAsync(
        Guid principalId, Guid? addressBookId = null, double? maxScore = null, int? take = null, CancellationToken ct = default)
    {
        if (maxScore is < 0 or > 1) return OpResult<List<ContactDto>>.Invalid("maxScore must be between 0 and 1.");
        if (take is < 1) return OpResult<List<ContactDto>>.Invalid("take must be >= 1.");

        var bookIds = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        if (addressBookId is { } abid)
        {
            if (!bookIds.Contains(abid)) return OpResult<List<ContactDto>>.Forbidden("No access to this address book.");
            bookIds = [abid];
        }

        var candidates = await session.Query<Contact>().Where(c => c.DeletedAt == null).ToListAsync(ct);
        var contacts = candidates.Where(c => bookIds.Contains(c.AddressBookId)).ToList();
        var scores = await completeness.ScoreContactsAsync(contacts, ct);
        var thin = contacts
            .Select(c => (Contact: c, Score: scores[c.Id]))
            .Where(x => x.Score is not null && x.Score.Score < (maxScore ?? 1.0))
            .OrderBy(x => x.Score!.Score).ThenBy(x => x.Contact.SortName)
            .Take(take ?? 25);
        return OpResult<List<ContactDto>>.Ok([.. thin.Select(x => x.Contact.ToResponse(x.Score))]);
    }

    // ---- Profiles + addresses (wholesale replace) ----

    public async Task<OpResult<ContactDto>> SetProfilesAsync(Guid principalId, Guid id, IReadOnlyList<ContactSocialProfileInput> profiles, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var normalized = profiles.Select(p => SocialProfileNormalizer.Normalize(p.ToDomain())).ToList();
        if (normalized.Any(p => p.Service.Length == 0 || p.Handle.Length == 0)) return OpResult<ContactDto>.Invalid("Profile service and handle are required.");
        var next = normalized.DistinctBy(p => (p.Service, Handle: p.Handle.ToLowerInvariant())).ToList();
        if (next.GroupBy(p => p.Service).Any(g => g.Count(p => p.Preferred) > 1)) return OpResult<ContactDto>.Invalid("At most one preferred handle per service.");
        if (ProfilesEqual(c.Profiles, next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactProfilesReplaced(id, next, occurredAt, commandId));
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
        return OpResult<ContactDto>.Ok(await ToDtoAsync((await session.LoadAsync<Contact>(id, ct))!, ct));
    }

    public async Task<OpResult<ContactDto>> SetAddressesAsync(Guid principalId, Guid id, IReadOnlyList<ContactPostalAddress> addresses, DateTimeOffset? occurredAt = null, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return await ReplayedAsync(id, ct);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        if (c is null || c.DeletedAt is not null) return OpResult<ContactDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactDto>.Forbidden("No write access to this contact.");

        var next = addresses.Select(a => new ContactPostalAddress { PlaceId = a.PlaceId, Type = a.Type, MovedIn = a.MovedIn, MovedOut = a.MovedOut }).ToList();
        if (next.Any(a => a.PlaceId == Guid.Empty)) return OpResult<ContactDto>.Invalid("Each address must reference a geo place id.");
        if (next.Any(a => (a.MovedIn is { } mi && !mi.IsValid()) || (a.MovedOut is { } mo && !mo.IsValid())))
            return OpResult<ContactDto>.Invalid("Residency dates must be a valid year, year-month, or year-month-day.");
        if (next.Any(a => a is { MovedIn: { } mi, MovedOut: { } mo } && FuzzyDate.DefinitelyAfter(mi, mo)))
            return OpResult<ContactDto>.Invalid("Moved-in must not be after moved-out.");
        if (AddressesEqual(c.Addresses, next)) return OpResult<ContactDto>.Ok(await ToDtoAsync(c, ct));
        Stamp(principalId);

        stream.AppendOne(new ContactAddressesReplaced(id, next, occurredAt, commandId));   // addresses are outside the canonical content — ETag unchanged
        await SaveGuardedAsync(commandId, id, (int) (stream.CurrentVersion ?? 0) + 1, ct);
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
        var byId = all.ToDictionary(x => x.Id);
        var relationships = (await session.LiveRelationshipsAsync(ct)).Where(r => byId.ContainsKey(r.Low) && byId.ContainsKey(r.High)).ToList();
        var organizations = await session.Query<ContactGroup>()
            .Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt == null).ToListAsync(ct);

        var memberships = CircleInference.Infer(fid, all, relationships, organizations);
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

    /// <summary>Links a principal that has no live, readable self-contact to one: an email match among the contacts it can
    /// read (<see cref="MatchSelfContactAsync"/>), else a new contact in its own personal book (<see cref="AddressBook.PersonalOf"/>,
    /// which <see cref="AddressBookService.EnsurePersonalBookAsync"/> provides). Returns the linked id.</summary>
    public async Task<Guid?> EnsureSelfContactAsync(Guid principalId, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Principal>(principalId, ct) is not { } principal) return null;
        if (principal.ContactId is { } linked && await session.LoadAsync<Contact>(linked, ct) is { DeletedAt: null } c
            && await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct))
            return linked;

        return await LinkByEmailAsync(principal, await access.GrantsAsync(principalId, ct), ct) ?? await CreateSelfContactAsync(principal, ct);
    }

    /// <summary>Links the principal to a contact it can read carrying its login email, if one exists — never creates. Several
    /// matches resolve to one in a book the principal owns, then the most recently updated.</summary>
    public async Task<Guid?> MatchSelfContactAsync(Guid principalId, CancellationToken ct = default) =>
        await session.LoadAsync<Principal>(principalId, ct) is { } principal
            ? await LinkByEmailAsync(principal, await access.GrantsAsync(principalId, ct), ct)
            : null;

    private async Task<Guid?> LinkByEmailAsync(Principal principal, List<AddressBookOwner> grants, CancellationToken ct)
    {
        var email = principal.Email.Trim();
        if (email.Length == 0) return null;
        var owned = grants.Where(g => g.Access == Access.Owner).Select(g => g.AddressBookId).ToHashSet();
        var matches = await LiveContactsIn([.. grants.Select(g => g.AddressBookId)])
            .Where(c => c.Channels.Any(ch => ch.Medium == ReachMedium.Email && ch.Value.Equals(email, StringComparison.OrdinalIgnoreCase)))
            .ToListAsync(ct);
        var pick = matches
            .OrderByDescending(c => owned.Contains(c.AddressBookId))
            .ThenByDescending(c => c.UpdatedSequence)
            .ThenBy(c => c.Id)
            .FirstOrDefault();
        if (pick is null) return null;
        await StoreSelfLinkAsync(principal, pick.Id, ct);
        return pick.Id;
    }

    private async Task<Guid?> CreateSelfContactAsync(Principal principal, CancellationToken ct)
    {
        var personal = await session.Query<AddressBook>().Where(b => b.PersonalOf == principal.Id).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        var (given, family) = SelfContactName.From(principal.DisplayName, principal.Email);
        if (personal is null || given is null) return null;

        var email = principal.Email.Trim();
        var request = new CreateContactRequest
        {
            AddressBookId = personal.Id,
            SourceKey = $"self-{principal.Id:N}@cal.lupira.com",   // one stream per principal, so racing bootstraps converge on one contact
            GivenName = given,
            FamilyName = family,
            Channels = email.Length == 0 ? null : [new ContactReachChannel(ReachMedium.Email, email, null, true)],
        };
        OpResult<ContactDto> created;
        try
        {
            created = await CreateAsync(principal.Id, request, ct);
        }
        catch (Exception ex) when (ex is ConcurrencyException or Marten.Exceptions.ExistingStreamIdCollisionException)
        {
            session.EjectAllPendingChanges();
            created = await CreateAsync(principal.Id, request, ct);   // the winner's contact, as an idempotent hit
        }

        if (created.Value is not { } contact) return null;
        await StoreSelfLinkAsync(principal, contact.Id, ct);
        return contact.Id;
    }

    private async Task StoreSelfLinkAsync(Principal principal, Guid contactId, CancellationToken ct)
    {
        principal.ContactId = contactId;
        session.Store(principal);
        await session.SaveChangesAsync(ct);
    }

    // Order-sensitive equality: order is part of the canonical content, so a reorder is a real change.
    private static bool ProfilesEqual(IReadOnlyList<ContactSocialProfile>? a, IReadOnlyList<ContactSocialProfile> b) =>
        (a ?? []).Select(p => (p.Service, p.Handle, p.Url, p.Preferred)).SequenceEqual(b.Select(p => (p.Service, p.Handle, p.Url, p.Preferred)));

    private static bool AddressesEqual(IReadOnlyList<ContactPostalAddress>? a, IReadOnlyList<ContactPostalAddress> b) =>
        (a ?? []).Select(x => (x.PlaceId, x.Type, x.MovedIn, x.MovedOut)).SequenceEqual(b.Select(x => (x.PlaceId, x.Type, x.MovedIn, x.MovedOut)));

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
        var card = live ? await cards.ForAsync(principalId, existing!, ct) : null;
        if (ifMatch is not null && card?.Etag != ifMatch) return OpResult<DavWriteResult>.Conflict("ETag mismatch.");
        Stamp(principalId);

        var p = VCardSerializer.ParseVCard(rawVcard);
        // Every section below is preserve-if-absent: clients drop what they don't model, so wholesale replace would
        // clear it on every sync. This surface can therefore set but never clear; the REST endpoints clear.
        // DisplayNameFormat isn't a vCard field at all — always preserve.
        var fields = new ContactFields(p.GivenName, null, p.FamilyName, null,
            p.Channels is null ? null : ReachChannelNormalizer.Normalize(p.Channels), p.Birthday, null,
            p.Notes ?? existing?.Notes, p.Pronouns ?? existing?.Pronouns, existing?.DisplayNameFormat ?? DisplayNameFormat.FirstLast,
            p.Kind ?? existing?.Kind ?? ContactKind.Individual);
        var emergency = p.EmergencyContactIds is not null
            ? p.EmergencyContactIds.Where(x => x != id).Distinct().ToList()
            : existing?.EmergencyContactIds ?? [];
        var deceased = p.Deceased ?? existing?.Deceased ?? false;
        var deathDate = p.Deceased is not null ? p.DeathDate : existing?.DeathDate;
        var profiles = p.Profiles is not null
            ? p.Profiles.Select(SocialProfileNormalizer.Normalize).Where(x => x.Service.Length > 0 && x.Handle.Length > 0).DistinctBy(x => (x.Service, Handle: x.Handle.ToLowerInvariant())).ToList()
            : existing?.Profiles ?? [];

        stream.AppendOne(new ContactImported(id, addressBookId, externalId, fields));   // also clears soft-delete
        if (!(existing?.EmergencyContactIds ?? []).SequenceEqual(emergency)) stream.AppendOne(new ContactEmergencyContactsReplaced(id, emergency));
        if (!ProfilesEqual(existing?.Profiles, profiles)) stream.AppendOne(new ContactProfilesReplaced(id, profiles));
        if (deceased && (existing is null || !existing.Deceased || existing.DeathDate != deathDate)) stream.AppendOne(new ContactMarkedDeceased(id, deathDate));
        if (p.Relations is not null) await ReplaceShownRelationshipsAsync(id, card?.Relations ?? [], p.Relations, ct);
        await session.SaveChangesAsync(ct);
        // The ETag is derived from the final state (incl. preserved values), so it matches a subsequent GET.
        var saved = await session.LoadAsync<Contact>(id, ct);
        return OpResult<DavWriteResult>.Ok(new DavWriteResult(!live, (await cards.ForAsync(principalId, saved!, ct)).Etag));
    }

    // A card's relations replace the relationships it showed. Each line states one as seen from this contact: its label is this
    // contact's word, and the note — not on the card — is kept. One the card no longer lists is removed. Lax like the rest of
    // the import: unresolvable others are stored as-is (reads filter) and parent cycles are tolerated (inference is bounded).
    private async Task ReplaceShownRelationshipsAsync(Guid id, IReadOnlyList<ResolvedRelation> shown, IReadOnlyList<ResolvedRelation> lines, CancellationToken ct)
    {
        var stated = lines.Where(l => l.OtherId != id).DistinctBy(l => (l.OtherId, l.Kind)).ToList();
        foreach (var line in stated)
        {
            var key = RelationshipKey.Of(id, line.OtherId, line.Kind);
            var stream = await session.Events.FetchForWriting<Relationship>(key.StreamId, ct);
            var note = stream.Aggregate is { IsLive: true } current ? current.Note : null;
            var events = Relationship.Upsert(stream.Aggregate, key, id, line.Label, line.Since, note, line.Ended, line.Until);
            if (events.Count > 0) stream.AppendMany(events);
        }

        foreach (var dropped in shown.Where(v => !stated.Any(l => l.OtherId == v.OtherId && l.Kind == v.Kind)))
        {
            var stream = await session.Events.FetchForWriting<Relationship>(RelationshipKey.Of(id, dropped.OtherId, dropped.Kind).StreamId, ct);
            if (stream.Aggregate is { IsLive: true } current) stream.AppendMany(current.Remove());
        }
    }

    public async Task<OpResult> DeleteByUidAsync(Guid principalId, Guid addressBookId, string externalId, string? ifMatch, CancellationToken ct = default)
    {
        if (!await access.CanWriteAddressBookAsync(principalId, addressBookId, ct)) return OpResult.Forbidden("No write access to this address book.");
        var id = DeterministicGuid.From(externalId);
        var stream = await session.Events.FetchForWriting<Contact>(id, ct);
        var c = stream.Aggregate;
        // c.AddressBookId != addressBookId guards the uid-collision case (the contact lives in another book).
        if (c is null || c.DeletedAt is not null || c.AddressBookId != addressBookId) return OpResult.NotFound();
        if (ifMatch is not null && (await cards.ForAsync(principalId, c, ct)).Etag != ifMatch) return OpResult.Conflict("ETag mismatch.");
        // Not Conflict: on this surface that is the precondition failure (412), which a client answers by refetching and retrying.
        if (await SelfContactRefusalAsync(c, ct) is { } refusal) return OpResult.Forbidden(refusal);
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
}
