using LupiraContactApi.Core.Application.Results;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// Residencies for clients: those whose contact is live and readable by the caller. The changes feed also re-sends the
/// residencies of every contact touched past the cursor, because a contact's deletion or move changes what is visible
/// without touching the residency. Unpaged — a family's residencies fit one response.
/// </summary>
public sealed class ResidencyFeed(IQuerySession session, AccessResolver access)
{
    public async Task<IReadOnlyList<ResidencyDto>> VisibleAsync(Guid principalId, CancellationToken ct = default)
    {
        var visibleContacts = await session.LiveContactIdsInAsync(await access.AccessibleAddressBookIdsAsync(principalId, ct), ct);
        return [.. (await session.LiveResidenciesAsync(ct)).Where(r => visibleContacts.Contains(r.ContactId)).Select(r => r.ToResponse())];
    }

    public async Task<OpResult<ResidencyChangesResponse>> ChangesAsync(Guid principalId, string? since, CancellationToken ct = default)
    {
        // Read the watermark first: anything committed while this runs is re-sent next time rather than missed.
        var token = await session.LatestSequenceAsync(ct);
        var readable = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var scope = SyncCursor.ScopeOf(readable);
        if (!SyncCursor.TryResume(since, scope, out var cursor))
            return OpResult<ResidencyChangesResponse>.Invalid("since must be a cursor previously returned by this endpoint (or omitted for a full sync).");
        var reset = cursor == 0;
        var visibleContacts = await session.LiveContactIdsInAsync(readable, ct);

        IReadOnlyList<Residency> rows;
        if (reset)
        {
            rows = await session.LiveResidenciesAsync(ct);
        }
        else
        {
            var touched = await session.Query<Residency>().Where(r => r.UpdatedSequence > cursor).ToListAsync(ct);
            var touchedContacts = await session.ContactsTouchedSinceAsync(cursor, ct);
            rows = [.. touched.Concat(await session.ResidenciesOfAsync(touchedContacts, ct)).DistinctBy(r => r.Id)];
        }

        var changed = rows.Where(r => r.IsLive && visibleContacts.Contains(r.ContactId)).ToList();
        return OpResult<ResidencyChangesResponse>.Ok(new ResidencyChangesResponse
        {
            Cursor = new SyncCursor(token, scope).ToString(),
            Reset = reset,
            Changed = [.. changed.Select(r => r.ToResponse())],
            Deleted = reset ? [] : [.. rows.Except(changed).Select(r => r.Id)],
        });
    }
}
