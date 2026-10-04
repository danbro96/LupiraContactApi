using LupiraContactApi.Core.Domain.Residencies;
using Marten;

namespace LupiraContactApi.Core.Data;

/// <summary>Live-residency reads by contact or place, over the indexed <see cref="Residency.ContactId"/>/<see cref="Residency.PlaceId"/>.</summary>
public static class ResidencyQueries
{
    public static async Task<IReadOnlyList<Residency>> ResidenciesOfAsync(this IQuerySession session, Guid contactId, CancellationToken ct = default) =>
        await session.Query<Residency>().Where(r => !r.Removed && r.ContactId == contactId).ToListAsync(ct);

    public static async Task<IReadOnlyList<Residency>> ResidenciesOfAsync(this IQuerySession session, IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default)
    {
        if (contactIds.Count == 0) return [];
        var ids = contactIds.ToArray();
        return await session.Query<Residency>().Where(r => !r.Removed && ids.Contains(r.ContactId)).ToListAsync(ct);
    }

    public static async Task<IReadOnlyList<Residency>> ResidenciesAtAsync(this IQuerySession session, IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default)
    {
        if (placeIds.Count == 0) return [];
        var ids = placeIds.ToArray();
        return await session.Query<Residency>().Where(r => !r.Removed && ids.Contains(r.PlaceId)).ToListAsync(ct);
    }

    public static async Task<IReadOnlyList<Residency>> LiveResidenciesAsync(this IQuerySession session, CancellationToken ct = default) =>
        await session.Query<Residency>().Where(r => !r.Removed).ToListAsync(ct);
}
