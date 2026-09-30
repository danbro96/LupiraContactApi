namespace LupiraContactApi.Core.Domain.Relationships;

/// <summary>Merges stored copies into relationships seen from one contact. The label is per side, so it comes from the viewer's own
/// copy; relationship fields come from the Low contact's copy, falling back to the other, so both sides agree even where legacy
/// copies disagree. Ended only when every copy is — a live copy still asserts the relationship, as inference already reads it.</summary>
public static class RelationResolver
{
    public static IReadOnlyList<ResolvedRelation> Resolve(Guid viewerId, IEnumerable<RelationCopy> copies) =>
        [.. copies
            .Where(c => (c.HolderId == viewerId) ^ (c.Edge.ToContactId == viewerId))
            .GroupBy(RelationshipKey.Of)
            .Select(g => View(viewerId, g.Key, [.. g]))];

    public static ResolvedRelation View(Guid viewerId, RelationshipKey key, IReadOnlyCollection<RelationCopy> copies)
    {
        var own = copies.FirstOrDefault(c => c.HolderId == viewerId)?.Edge;
        var edges = copies.OrderBy(c => c.HolderId == key.Low ? 0 : 1).Select(c => c.Edge).ToList();
        var ended = edges.All(e => e.Ended);
        return new ResolvedRelation(
            key.OtherThan(viewerId),
            key.KindSeenFrom(viewerId),
            own?.Label,
            edges.Select(e => e.Since).FirstOrDefault(s => s is not null),
            edges.Select(e => e.Note).FirstOrDefault(n => n is not null),
            ended,
            ended ? edges.Select(e => e.Until).FirstOrDefault(u => u is not null) : null);
    }
}
