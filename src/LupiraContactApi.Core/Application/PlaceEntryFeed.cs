using Lupira.Results;
using Lupira.Sync;
using Lupira.Sync.Marten;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.PlaceEntries;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Core.Mappers;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// Entry codes for offline clients: a place's codes are visible while the caller can read a live contact currently living
/// there. That depends on residencies and contacts, so a delta also re-sends the places whose residencies, or whose
/// residents, changed since the cursor. Tombstones are place ids. A move-out date passing changes nothing here until the
/// next full sync, so clients show codes only where their own mirror still has a current resident.
/// </summary>
public sealed class PlaceEntryFeed(IQuerySession session, AccessResolver access, IOptions<SyncFeedOptions> options)
{
    private readonly TimeSpan _settleLag = options.Value.SettleLag;

    public async Task<OpResult<SyncPage<PlaceEntryDto>>> ChangesAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        if (!SyncFeedQuery.TryParse(since, limit, out var query))
            return OpResult<SyncPage<PlaceEntryDto>>.Invalid(SyncFeedQuery.InvalidSince);

        var readable = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var scope = SyncCursor.ScopeOf(readable);
        var visibleContacts = await session.LiveContactIdsInAsync(readable, ct);
        return OpResult<SyncPage<PlaceEntryDto>>.Ok(query.IsFullSync(scope)
            ? await FullSyncAsync(query, scope, visibleContacts, ct)
            : await DeltaAsync(query, scope, readable, visibleContacts, ct));
    }

    private async Task<SyncPage<PlaceEntryDto>> FullSyncAsync(SyncFeedQuery query, string scope, HashSet<Guid> visibleContacts, CancellationToken ct)
    {
        var (head, after) = await session.FullSyncFromAsync(query, scope, _settleLag, ct);
        var places = VisiblePlaces(await session.ResidenciesOfAsync(visibleContacts, ct), visibleContacts).ToArray();
        var visible = session.Query<PlaceEntry>().Where(e => places.Contains(e.PlaceId) && e.Codes.Any());
        if (after is { } last) visible = visible.Where(e => e.Id > last);
        var rows = (await visible.OrderBy(e => e.Id).Take(query.Limit + 1).ToListAsync(ct)).ToList();
        var resumeAfter = SyncPages.TrimToPage(rows, query.Limit, e => e.Id);
        return SyncPages.Full([.. rows.Select(e => e.ToResponse())], head, scope, query.IsReset(scope), resumeAfter);
    }

    private async Task<SyncPage<PlaceEntryDto>> DeltaAsync(SyncFeedQuery query, string scope, List<Guid> readable, HashSet<Guid> visibleContacts, CancellationToken ct)
    {
        var since = query.Since!.Value.Sequence;
        var head = await session.HeadSequenceAsync(_settleLag, ct);
        var own = await session.ChangedStreamsAsync<PlaceEntry>(since, query.Limit, _settleLag, ct);
        var residencies = await session.ChangedStreamsAsync<Residency>(since, query.Limit, _settleLag, ct);
        var contacts = await session.ChangedStreamsAsync<Contact>(since, query.Limit, _settleLag, ct);

        var touchedPlaces = (await session.LoadManyAsync<Residency>(ct, residencies.Ids))
            .Concat(await session.ResidenciesOfAsync(contacts.Ids, ct))
            .Select(r => r.PlaceId)
            .ToHashSet();
        var entries = await session.LoadManyAsync<PlaceEntry>(ct, own.Ids.Concat(touchedPlaces.Select(PlaceEntry.StreamIdOf)).Distinct());
        var places = entries.Select(e => e.PlaceId).ToArray();
        // Removed residencies count too: a place stays known to whoever could once see a resident there.
        var residents = await session.Query<Residency>().Where(r => places.Contains(r.PlaceId)).ToListAsync(ct);
        var known = await session.EverReadableContactIdsAsync(residents.Select(r => r.ContactId), readable, ct);
        var knownPlaces = residents.Where(r => known.Contains(r.ContactId)).Select(r => r.PlaceId).ToHashSet();
        var rows = entries.Where(e => knownPlaces.Contains(e.PlaceId)).ToList();
        var visiblePlaces = VisiblePlaces(residents.Where(r => !r.Removed), visibleContacts);

        var changed = rows.Where(e => e.Codes.Count > 0 && visiblePlaces.Contains(e.PlaceId)).ToList();
        return SyncPages.Delta([.. changed.Select(e => e.ToResponse())], [.. rows.Except(changed).Select(e => e.PlaceId)], scope, head, own, residencies, contacts);
    }

    private static HashSet<Guid> VisiblePlaces(IEnumerable<Residency> residencies, HashSet<Guid> visibleContacts)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return [.. residencies.Where(r => r.IsActiveOn(today) && visibleContacts.Contains(r.ContactId)).Select(r => r.PlaceId)];
    }
}
