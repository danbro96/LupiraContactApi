using JasperFx.Events;
using Lupira.Identity.Marten;
using LupiraContactApi.Core.Domain.Relationships.Events;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Relationships;

/// <summary>
/// A relationship between two contacts + inline snapshot: "<see cref="High"/> is <see cref="Low"/>'s <see cref="Kind"/>",
/// keyed by <see cref="RelationshipKey"/>, so it exists once and belongs to neither contact. Each side keeps its own label
/// (its word for the other); the rest is shared. No FK to the contacts — a deleted or unreadable side is filtered on read.
/// </summary>
public sealed class Relationship
{
    public Guid Id { get; set; }

    public Guid Low { get; set; }

    public Guid High { get; set; }

    public ContactRelationKind Kind { get; set; }

    public string? LabelFromLow { get; set; }

    public string? LabelFromHigh { get; set; }

    public DateOnly? Since { get; set; }

    public string? Note { get; set; }

    public bool Ended { get; set; }

    public DateOnly? Until { get; set; }

    /// <summary>Erased as a mistake; kept as a tombstone so sync feeds report the removal.</summary>
    public bool Removed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public RelationshipKey Key => new(Low, High, Kind);

    /// <summary>Live: established and not removed. Ended relationships are live — they stay listed, flagged.</summary>
    public bool IsLive => Id != Guid.Empty && !Removed;

    /// <summary>The events that bring the relationship at <paramref name="key"/> to the stated state, as told by
    /// <paramref name="viewerId"/>: <paramref name="label"/> is the viewer's own word, the rest is shared. Empty when it already
    /// holds. A missing or removed relationship is established afresh.</summary>
    public static IReadOnlyList<object> Upsert(
        Relationship? current, RelationshipKey key, Guid viewerId, string? label, DateOnly? since, string? note,
        bool ended = false, DateOnly? until = null)
    {
        var id = key.StreamId;
        var events = new List<object>();
        if (current is not { IsLive: true })
        {
            events.Add(new RelationshipEstablished(id, key.Low, key.High, key.Kind, since, note));
            if (label is not null) events.Add(new RelationshipLabelled(id, viewerId, label));
            if (ended) events.Add(new RelationshipEnded(id, until));
            return events;
        }

        if (current.Ended && !ended) events.Add(new RelationshipRevived(id));
        if (current.Since != since || current.Note != note) events.Add(new RelationshipRevised(id, since, note));
        if (current.LabelFrom(viewerId) != label) events.Add(new RelationshipLabelled(id, viewerId, label));
        if (ended && !(current.Ended && current.Until == until)) events.Add(new RelationshipEnded(id, until));
        return events;
    }

    public bool Involves(Guid contactId) => contactId == Low || contactId == High;

    public string? LabelFrom(Guid contactId) => contactId == Low ? LabelFromLow : LabelFromHigh;

    public ResolvedRelation ViewFrom(Guid contactId) =>
        new(Key.OtherThan(contactId), Key.KindSeenFrom(contactId), LabelFrom(contactId), Since, Note, Ended, Ended ? Until : null);

    public IReadOnlyList<object> End(DateOnly? until) => Ended && Until == until ? [] : [new RelationshipEnded(Id, until)];

    public IReadOnlyList<object> Remove() => [new RelationshipRemoved(Id)];

    public void Apply(IEvent<RelationshipEstablished> e)
    {
        var d = e.Data;
        (Id, Low, High, Kind, Since, Note) = (d.RelationshipId, d.Low, d.High, d.Kind, d.Since, d.Note);
        (LabelFromLow, LabelFromHigh, Ended, Until, Removed) = (null, null, false, null, false);
        CreatedAt = e.Timestamp;
        CreatedBy = EventActor.Of(e);
        Touch(e);
    }

    public void Apply(IEvent<RelationshipRevised> e)
    {
        (Since, Note) = (e.Data.Since, e.Data.Note);
        Touch(e);
    }

    public void Apply(IEvent<RelationshipLabelled> e)
    {
        if (e.Data.ContactId == Low) LabelFromLow = e.Data.Label;
        else if (e.Data.ContactId == High) LabelFromHigh = e.Data.Label;
        Touch(e);
    }

    public void Apply(IEvent<RelationshipEnded> e)
    {
        (Ended, Until) = (true, e.Data.Until);
        Touch(e);
    }

    public void Apply(IEvent<RelationshipRevived> e)
    {
        (Ended, Until) = (false, null);
        Touch(e);
    }

    public void Apply(IEvent<RelationshipRemoved> e)
    {
        Removed = true;
        Touch(e);
    }

    private void Touch(IEvent e)
    {
        UpdatedAt = e.Timestamp;
        UpdatedBy = EventActor.Of(e);
    }
}
