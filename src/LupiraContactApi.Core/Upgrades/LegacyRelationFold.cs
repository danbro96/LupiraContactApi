using JasperFx.Events;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Relationships.Events;

namespace LupiraContactApi.Core.Upgrades;

/// <summary>Turns the legacy per-contact relation copies into relationships. Pure, so the migration's two halves —
/// replaying the old events, and merging each pair of copies — are testable without a store.</summary>
public static class LegacyRelationFold
{
    /// <summary>Each contact's relation copies after replaying its legacy events in sequence order, exactly as the old
    /// contact snapshot applied them.</summary>
    public static IReadOnlyDictionary<Guid, List<ContactRelation>> Copies(IEnumerable<IEvent> events)
    {
        var copies = new Dictionary<Guid, List<ContactRelation>>();
        List<ContactRelation> Of(Guid holder) => copies.TryGetValue(holder, out var list) ? list : copies[holder] = [];
        foreach (var e in events.OrderBy(e => e.Sequence))
        {
            switch (e.Data)
            {
                case ContactRelationAdded d:
                    Of(e.StreamId).RemoveAll(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind);
                    Of(e.StreamId).Add(new ContactRelation { ToContactId = d.ToContactId, Kind = d.Kind, Label = d.Label, Since = d.Since, Note = d.Note });
                    break;
                case ContactRelationEnded d when Of(e.StreamId).FirstOrDefault(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind) is { } edge:
                    (edge.Ended, edge.Until) = (true, d.Until);
                    break;
                case ContactRelationRemoved d:
                    Of(e.StreamId).RemoveAll(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind);
                    break;
                case ContactRelationsReplaced d:
                    copies[e.StreamId] = [.. d.Relations.Select(r => new ContactRelation
                        { ToContactId = r.ToContactId, Kind = r.Kind, Label = r.Label, Since = r.Since, Note = r.Note, Ended = r.Ended, Until = r.Until })];
                    break;
            }
        }

        return copies;
    }

    /// <summary>One relationship per key, with the events that establish its merged state: each side's label from its own
    /// copy, since/note/until from the Low contact's copy first, ended only when every copy is.</summary>
    public static IReadOnlyList<(Guid StreamId, IReadOnlyList<object> Events)> Relationships(IReadOnlyDictionary<Guid, List<ContactRelation>> copies) =>
        [.. copies
            .SelectMany(c => c.Value.Where(r => r.ToContactId != c.Key).Select(r => (Holder: c.Key, Edge: r)))
            .GroupBy(x => RelationshipKey.Of(x.Holder, x.Edge.ToContactId, x.Edge.Kind))
            .Select(g => (g.Key.StreamId, Merge(g.Key, [.. g])))];

    private static IReadOnlyList<object> Merge(RelationshipKey key, IReadOnlyList<(Guid Holder, ContactRelation Edge)> held)
    {
        var edges = held.OrderBy(x => x.Holder == key.Low ? 0 : 1).Select(x => x.Edge).ToList();
        var ended = edges.All(e => e.Ended);
        string? LabelOf(Guid side) => held.FirstOrDefault(x => x.Holder == side).Edge?.Label;

        var events = Relationship.Upsert(null, key, key.Low, LabelOf(key.Low),
            edges.Select(e => e.Since).FirstOrDefault(s => s is not null),
            edges.Select(e => e.Note).FirstOrDefault(n => n is not null),
            ended, ended ? edges.Select(e => e.Until).FirstOrDefault(u => u is not null) : null).ToList();
        if (LabelOf(key.High) is { } highLabel) events.Add(new RelationshipLabelled(key.StreamId, key.High, highLabel));
        return events;
    }
}
