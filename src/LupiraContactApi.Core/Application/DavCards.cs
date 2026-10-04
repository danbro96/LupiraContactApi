using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Builds <see cref="DavCard"/>s. A card lists a relationship only when its other contact is live and readable by
/// the caller — the same visibility as the REST listing.</summary>
public sealed class DavCards(IQuerySession session, AccessResolver access)
{
    public async Task<DavCard> ForAsync(Guid principalId, Contact contact, CancellationToken ct = default) =>
        (await ForAsync(principalId, [contact], ct))[0];

    public async Task<IReadOnlyList<DavCard>> ForAsync(Guid principalId, IReadOnlyCollection<Contact> contacts, CancellationToken ct = default)
    {
        var relationships = await session.RelationshipsOfAsync([.. contacts.Select(c => c.Id)], ct);
        var otherIds = relationships.SelectMany(r => new[] { r.Low, r.High }).Distinct().ToArray();
        var books = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var visible = otherIds.Length == 0
            ? []
            : (await session.Query<Contact>().Where(c => otherIds.Contains(c.Id) && c.DeletedAt == null).ToListAsync(ct))
                .Where(c => books.Contains(c.AddressBookId)).Select(c => c.Id).ToHashSet();

        return [.. contacts.Select(c => new DavCard(c, [.. relationships
            .Where(r => r.Involves(c.Id) && visible.Contains(r.Key.OtherThan(c.Id)))
            .Select(r => r.ViewFrom(c.Id))
            .OrderBy(v => v.OtherId).ThenBy(v => v.Kind)]))];
    }
}
