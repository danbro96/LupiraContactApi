using LupiraContactApi.Core.Application.Results;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Dtos.Relationships;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// Relationships for clients: those whose two contacts are both live and readable by the caller. The changes feed also
/// re-sends the relationships of every contact touched past the cursor, because a contact's deletion or move changes
/// what is visible without touching the relationship. Unpaged — a family's relationships fit one response.
/// </summary>
public sealed class RelationshipFeed(IQuerySession session, AccessResolver access)
{
    public async Task<IReadOnlyList<RelationshipDto>> VisibleAsync(Guid principalId, CancellationToken ct = default)
    {
        var visibleContacts = await VisibleContactIdsAsync(await access.AccessibleAddressBookIdsAsync(principalId, ct), ct);
        return [.. (await session.LiveRelationshipsAsync(ct)).Where(r => IsVisible(r, visibleContacts)).Select(r => r.ToResponse())];
    }

    public async Task<OpResult<RelationshipChangesResponse>> ChangesAsync(Guid principalId, string? since, CancellationToken ct = default)
    {
        SyncCursor? given = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!SyncCursor.TryParse(since, out var parsed))
                return OpResult<RelationshipChangesResponse>.Invalid("since must be a cursor previously returned by this endpoint (or omitted for a full sync).");
            given = parsed;
        }

        // Read the watermark first: anything committed while this runs is re-sent next time rather than missed.
        var token = await session.LatestSequenceAsync(ct);
        var readable = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var scope = SyncCursor.ScopeOf(readable);
        var reset = given?.Scope != scope || given.Value.Sequence == 0;
        var visibleContacts = await VisibleContactIdsAsync(readable, ct);

        IReadOnlyList<Relationship> rows;
        if (reset)
        {
            rows = await session.LiveRelationshipsAsync(ct);
        }
        else
        {
            var cursor = given!.Value.Sequence;
            var touched = await session.Query<Relationship>().Where(r => r.UpdatedSequence > cursor).ToListAsync(ct);
            var touchedContacts = await session.Query<Contact>().Where(c => c.UpdatedSequence > cursor).Select(c => c.Id).ToListAsync(ct);
            rows = [.. touched.Concat(await session.RelationshipsOfAsync(touchedContacts, ct)).DistinctBy(r => r.Id)];
        }

        var changed = rows.Where(r => r.IsLive && IsVisible(r, visibleContacts)).ToList();
        return OpResult<RelationshipChangesResponse>.Ok(new RelationshipChangesResponse
        {
            Cursor = new SyncCursor(token, scope).ToString(),
            Reset = reset,
            Changed = [.. changed.Select(r => r.ToResponse())],
            Deleted = reset ? [] : [.. rows.Except(changed).Select(r => r.Id)],
        });
    }

    private static bool IsVisible(Relationship r, HashSet<Guid> visibleContacts) => visibleContacts.Contains(r.Low) && visibleContacts.Contains(r.High);

    private async Task<HashSet<Guid>> VisibleContactIdsAsync(IReadOnlyCollection<Guid> readableBooks, CancellationToken ct)
    {
        var books = readableBooks.ToArray();
        return [.. await session.Query<Contact>().Where(c => c.DeletedAt == null && books.Contains(c.AddressBookId)).Select(c => c.Id).ToListAsync(ct)];
    }
}
