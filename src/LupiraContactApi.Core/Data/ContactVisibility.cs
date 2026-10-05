using LupiraContactApi.Core.Domain.Contacts;
using Marten;

namespace LupiraContactApi.Core.Data;

public static class ContactVisibility
{
    /// <summary>Ids of the live contacts in <paramref name="readableBooks"/> — what the feeds may show things about.</summary>
    public static async Task<HashSet<Guid>> LiveContactIdsInAsync(this IQuerySession session, IReadOnlyCollection<Guid> readableBooks, CancellationToken ct = default)
    {
        var books = readableBooks.ToArray();
        return [.. await session.Query<Contact>().Where(c => c.DeletedAt == null && books.Contains(c.AddressBookId)).Select(c => c.Id).ToListAsync(ct)];
    }

    /// <summary>Of <paramref name="contactIds"/>, those in or moved out of <paramref name="readableBooks"/>, deleted or not — the
    /// contacts the caller may have seen, so only rows about them are tombstoned to it.</summary>
    public static async Task<HashSet<Guid>> EverReadableContactIdsAsync(
        this IQuerySession session, IEnumerable<Guid> contactIds, IReadOnlyCollection<Guid> readableBooks, CancellationToken ct = default)
    {
        var (ids, books) = (contactIds.Distinct().ToArray(), readableBooks.ToArray());
        if (ids.Length == 0) return [];
        return [.. await session.Query<Contact>()
            .Where(c => ids.Contains(c.Id) && (books.Contains(c.AddressBookId) || c.FormerAddressBookIds.Any(b => books.Contains(b))))
            .Select(c => c.Id)
            .ToListAsync(ct)];
    }
}
