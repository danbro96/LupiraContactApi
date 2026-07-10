using LupiraContactApi.Domain;
using Marten;

namespace LupiraContactApi.Application;

/// <summary>A contact whose state changed since a sync token: its resource UID and current ETag, or a tombstone.</summary>
public sealed record DavChange(string Uid, string? Etag, bool Deleted);

/// <summary>The CardDAV change feed backing the <c>/dav-backend</c> seam: sync tokens are Marten's global event
/// sequence (opaque to the gateway), changes are the contact streams touched past a token, deletions are tombstones.</summary>
public sealed class DavChangeFeed(IQuerySession session)
{
    /// <summary>The current sync token = the store's latest global event sequence.</summary>
    public async Task<long> CurrentTokenAsync(CancellationToken ct = default)
    {
        var last = await session.Events.QueryAllRawEvents().OrderByDescending(e => e.Sequence).Take(1).ToListAsync(ct);
        return last.Count > 0 ? last[0].Sequence : 0L;
    }

    /// <summary>Changes in an address book since <paramref name="since"/>; a null/unparsable token yields the
    /// full live listing (self-healing resync). Deletions surface as tombstones only on incremental diffs.</summary>
    public async Task<(long Token, IReadOnlyList<DavChange> Changes)> ChangesSinceAsync(Guid addressBookId, long? since, CancellationToken ct = default)
    {
        var newToken = await CurrentTokenAsync(ct);

        if (since is null)
        {
            var live = await session.Query<Contact>().Where(c => c.AddressBookId == addressBookId && c.DeletedAt == null).ToListAsync(ct);
            return (newToken, [.. live.Select(c => new DavChange(c.ExternalId, c.ContentHash, Deleted: false))]);
        }

        var changedIds = (await session.Events.QueryAllRawEvents().Where(e => e.Sequence > since).ToListAsync(ct))
            .Select(e => e.StreamId).Distinct().ToList();
        var contacts = await session.Query<Contact>().Where(c => changedIds.Contains(c.Id) && c.AddressBookId == addressBookId).ToListAsync(ct);
        return (newToken, [.. contacts.Select(c => c.DeletedAt is not null
            ? new DavChange(c.ExternalId, null, Deleted: true)
            : new DavChange(c.ExternalId, c.ContentHash, Deleted: false))]);
    }
}
