using Lupira.Results;
using Lupira.Sync;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// The offline-client changes feed (LupiraCalApi's contract): contacts in, or moved out of, an address book the
/// caller can read, paged by <c>UpdatedSequence</c> (index-backed). Deletes and moves out surface as tombstones on
/// deltas; an access change restarts the stream (<see cref="SyncCursor"/>). Pre-watermark documents need one
/// <c>--rebuild-contacts</c>.
/// </summary>
public sealed class SyncFeed(IQuerySession session, AccessResolver access, CompletenessResolver completeness)
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;

    public async Task<OpResult<SyncChangesResponse>> ChangesAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        SyncCursor? given = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!SyncCursor.TryParse(since, out var parsed))
                return OpResult<SyncChangesResponse>.Invalid("since must be a cursor previously returned by this endpoint (or omitted for a full sync).");
            given = parsed;
        }
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var readable = (await access.AccessibleAddressBookIdsAsync(principalId, ct)).ToArray();
        var scope = SyncCursor.ScopeOf(readable);
        var reset = given?.Scope != scope;
        var cursor = reset ? 0 : given!.Value.Sequence;
        var fullSync = cursor == 0;

        // A moved contact still matches via the book it left, so that book's readers get the tombstone.
        var page = await session.Query<Contact>()
            .Where(c => c.UpdatedSequence > cursor
                && (readable.Contains(c.AddressBookId) || c.FormerAddressBookIds.Any(b => readable.Contains(b))))
            .OrderBy(c => c.UpdatedSequence)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > take;
        var rows = hasMore ? page.Take(take).ToList() : page;

        var changed = new List<Contact>();
        var deleted = new List<Guid>();
        foreach (var c in rows)
        {
            var visibleLive = c.DeletedAt is null && readable.Contains(c.AddressBookId);
            if (visibleLive) changed.Add(c);
            // Full sync replaces the mirror wholesale, so tombstones would be noise.
            else if (!fullSync) deleted.Add(c.Id);
        }

        var scores = await completeness.ScoreContactsAsync(changed, ct);
        var next = rows.Count > 0 ? rows[^1].UpdatedSequence : cursor;
        return OpResult<SyncChangesResponse>.Ok(new SyncChangesResponse
        {
            Cursor = new SyncCursor(next, scope).ToString(),
            HasMore = hasMore,
            Reset = reset,
            Changed = [.. changed.Select(c => new SyncChangeDto { Contact = c.ToResponse(scores[c.Id]), Guards = SectionGuardsDto.From(c) })],
            Deleted = deleted,
        });
    }
}
