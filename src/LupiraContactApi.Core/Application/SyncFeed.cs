using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// The offline-client changes feed (same contract as LupiraCalApi's): account-wide, paged strictly by each
/// contact's <c>UpdatedSequence</c> watermark (index-backed — one document query, never a raw-event scan).
/// Deletions and visibility losses (contact moved to an unreadable book, book unshared) surface as tombstone ids
/// on incremental pulls. Requires the contact projection to be rebuilt once after deploy
/// (<c>--rebuild-contacts</c>) so pre-existing documents carry a watermark.
/// </summary>
public sealed class SyncFeed(IQuerySession session, AccessResolver access, CompletenessResolver completeness)
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;

    public async Task<OpResult<SyncChangesResponse>> ChangesAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        long cursor = 0;
        if (!string.IsNullOrWhiteSpace(since) && (!long.TryParse(since, out cursor) || cursor < 0))
            return OpResult<SyncChangesResponse>.Invalid("since must be a cursor previously returned by this endpoint (or omitted for a full sync).");
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var fullSync = cursor == 0;

        var visible = (await access.AccessibleAddressBookIdsAsync(principalId, ct)).ToHashSet();

        var page = await session.Query<Contact>()
            .Where(c => c.UpdatedSequence > cursor)
            .OrderBy(c => c.UpdatedSequence)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > take;
        var rows = hasMore ? page.Take(take).ToList() : page;

        var changed = new List<Contact>();
        var deleted = new List<Guid>();
        foreach (var c in rows)
        {
            var visibleLive = c.DeletedAt is null && visible.Contains(c.AddressBookId);
            if (visibleLive) changed.Add(c);
            // Full sync replaces the mirror wholesale, so tombstones would be noise; bare ids leak nothing.
            else if (!fullSync) deleted.Add(c.Id);
        }

        var scores = await completeness.ScoreContactsAsync(changed, ct);
        var next = rows.Count > 0 ? rows[^1].UpdatedSequence : cursor;
        return OpResult<SyncChangesResponse>.Ok(new SyncChangesResponse
        {
            Cursor = next.ToString(),
            HasMore = hasMore,
            Changed = [.. changed.Select(c => new SyncChangeDto { Contact = c.ToResponse(scores[c.Id]), Guards = SectionGuardsDto.From(c) })],
            Deleted = deleted,
        });
    }
}
