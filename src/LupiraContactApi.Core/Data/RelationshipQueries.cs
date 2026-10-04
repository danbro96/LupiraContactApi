using LupiraContactApi.Core.Domain.Relationships;
using Marten;

namespace LupiraContactApi.Core.Data;

/// <summary>Live-relationship reads by contact, over the indexed <see cref="Relationship.Low"/>/<see cref="Relationship.High"/>.</summary>
public static class RelationshipQueries
{
    public static async Task<IReadOnlyList<Relationship>> RelationshipsOfAsync(this IQuerySession session, Guid contactId, CancellationToken ct = default) =>
        await session.Query<Relationship>().Where(r => !r.Removed && (r.Low == contactId || r.High == contactId)).ToListAsync(ct);

    public static async Task<IReadOnlyList<Relationship>> RelationshipsOfAsync(this IQuerySession session, IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default)
    {
        if (contactIds.Count == 0) return [];
        var ids = contactIds.ToArray();
        return await session.Query<Relationship>().Where(r => !r.Removed && (ids.Contains(r.Low) || ids.Contains(r.High))).ToListAsync(ct);
    }

    public static async Task<IReadOnlyList<Relationship>> LiveRelationshipsAsync(this IQuerySession session, CancellationToken ct = default) =>
        await session.Query<Relationship>().Where(r => !r.Removed).ToListAsync(ct);
}
