using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>The CardDAV change feed backing the <c>/dav-backend</c> seam: sync tokens are Marten's global event
/// sequence (opaque to the gateway), changes are the contacts touched past a token — directly, or through a relationship
/// they are on — and deletions and moves out are tombstones.</summary>
public sealed class DavChangeFeed(IQuerySession session, DavCards cards)
{
    /// <summary>The current sync token = the store's latest global event sequence.</summary>
    public Task<long> CurrentTokenAsync(CancellationToken ct = default) => session.LatestSequenceAsync(ct);

    /// <summary>Changes in an address book since <paramref name="since"/>; a null/unparsable token yields the
    /// full live listing (self-healing resync). Deletions surface as tombstones only on incremental diffs.</summary>
    public async Task<(long Token, IReadOnlyList<DavChange> Changes)> ChangesSinceAsync(Guid principalId, Guid addressBookId, long? since, CancellationToken ct = default)
    {
        var newToken = await CurrentTokenAsync(ct);

        if (since is null)
        {
            var inBook = await session.Query<Contact>().Where(c => c.AddressBookId == addressBookId && c.DeletedAt == null).ToListAsync(ct);
            return (newToken, [.. (await cards.ForAsync(principalId, inBook, ct)).Select(card => new DavChange(card.Contact.ExternalId, card.Etag, Deleted: false))]);
        }

        var streamIds = (await session.Events.QueryAllRawEvents().Where(e => e.Sequence > since).ToListAsync(ct))
            .Select(e => e.StreamId).Distinct().ToList();
        // A relationship edit changes both cards it appears on — removed ones included.
        var relationshipEnds = (await session.Query<Relationship>().Where(r => streamIds.Contains(r.Id)).ToListAsync(ct))
            .SelectMany(r => new[] { r.Low, r.High });
        var changedIds = streamIds.Concat(relationshipEnds).Distinct().ToList();
        // A contact moved to another book is gone from this one, so it still matches here to be tombstoned.
        var contacts = await session.Query<Contact>()
            .Where(c => changedIds.Contains(c.Id) && (c.AddressBookId == addressBookId || c.FormerAddressBookIds.Contains(addressBookId)))
            .ToListAsync(ct);
        var gone = contacts.Where(c => c.DeletedAt is not null || c.AddressBookId != addressBookId).ToList();
        var present = await cards.ForAsync(principalId, contacts.Except(gone).ToList(), ct);
        return (newToken, [
            .. gone.Select(c => new DavChange(c.ExternalId, null, Deleted: true)),
            .. present.Select(card => new DavChange(card.Contact.ExternalId, card.Etag, Deleted: false)),
        ]);
    }
}
