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

    /// <summary>Ids of the contacts touched past <paramref name="sequence"/> — a delete or move changes what is visible about them.</summary>
    public static async Task<IReadOnlyList<Guid>> ContactsTouchedSinceAsync(this IQuerySession session, long sequence, CancellationToken ct = default) =>
        await session.Query<Contact>().Where(c => c.UpdatedSequence > sequence).Select(c => c.Id).ToListAsync(ct);
}
