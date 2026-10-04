using Lupira.Results;
using Lupira.Sync;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.PlaceEntries;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// Entry codes for offline clients: a place's codes are visible while the caller can read a live contact currently living
/// there. That depends on residencies and contacts, so the changes feed also re-sends the places whose residencies, or
/// whose residents, were touched past the cursor. A move-out date passing changes nothing here until the next full sync,
/// so clients show codes only where their own mirror still has a current resident.
/// </summary>
public sealed class PlaceEntryFeed(IQuerySession session, AccessResolver access)
{
    public async Task<OpResult<PlaceEntryChangesResponse>> ChangesAsync(Guid principalId, string? since, CancellationToken ct = default)
    {
        var token = await session.LatestSequenceAsync(ct);
        var readable = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var scope = SyncCursor.ScopeOf(readable);
        if (!SyncCursor.TryResume(since, scope, out var cursor))
            return OpResult<PlaceEntryChangesResponse>.Invalid("since must be a cursor previously returned by this endpoint (or omitted for a full sync).");
        var reset = cursor == 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var visibleContacts = await session.LiveContactIdsInAsync(readable, ct);
        var visiblePlaces = (await session.LiveResidenciesAsync(ct))
            .Where(r => r.IsActiveOn(today) && visibleContacts.Contains(r.ContactId)).Select(r => r.PlaceId).ToHashSet();

        IReadOnlyList<PlaceEntry> rows;
        if (reset)
        {
            rows = await session.Query<PlaceEntry>().ToListAsync(ct);
        }
        else
        {
            var touchedResidencies = await session.Query<Residency>().Where(r => r.UpdatedSequence > cursor).ToListAsync(ct);
            var touchedContacts = await session.ContactsTouchedSinceAsync(cursor, ct);
            var places = touchedResidencies.Concat(await session.ResidenciesOfAsync(touchedContacts, ct)).Select(r => r.PlaceId).Distinct().ToArray();
            rows = await session.Query<PlaceEntry>().Where(e => e.UpdatedSequence > cursor || places.Contains(e.PlaceId)).ToListAsync(ct);
        }

        var changed = rows.Where(e => e.Codes.Count > 0 && visiblePlaces.Contains(e.PlaceId)).ToList();
        return OpResult<PlaceEntryChangesResponse>.Ok(new PlaceEntryChangesResponse
        {
            Cursor = new SyncCursor(token, scope).ToString(),
            Reset = reset,
            Changed = [.. changed.Select(e => e.ToResponse())],
            Deleted = reset ? [] : [.. rows.Except(changed).Select(e => e.PlaceId)],
        });
    }
}
