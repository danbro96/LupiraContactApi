using JasperFx.Events;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Relationships.Events;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Upgrades;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The relationships migration: legacy per-contact copies replayed exactly as the old snapshot applied them, then
/// merged — a label per side, shared fields from the Low contact's copy first, ended only when every copy was.</summary>
public class LegacyRelationFoldTests
{
    private static readonly Guid Low = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid High = new("99999999-9999-9999-9999-999999999999");
    private static long _seq;

    private static IEvent Ev<T>(Guid stream, T data)
        where T : notnull
    {
        var e = Event.For(data);
        e.StreamId = stream;
        e.Sequence = Interlocked.Increment(ref _seq);
        return e;
    }

    private static Relationship Migrated(params IEvent[] legacy)
    {
        var (_, events) = Assert.Single(LegacyRelationFold.Relationships(LegacyRelationFold.Copies(legacy)));
        var r = new Relationship();
        foreach (var data in events)
        {
            switch (data)
            {
                case RelationshipEstablished d: r.Apply(Event.For(d)); break;
                case RelationshipLabelled d: r.Apply(Event.For(d)); break;
                case RelationshipEnded d: r.Apply(Event.For(d)); break;
            }
        }

        return r;
    }

    [Fact]
    public void Replays_upserts_removals_ends_and_wholesale_replaces()
    {
        var other = Guid.NewGuid();
        var copies = LegacyRelationFold.Copies(
        [
            Ev(Low, new ContactRelationAdded(Low, other, ContactRelationKind.Parent, "dad")),
            Ev(Low, new ContactRelationAdded(Low, other, ContactRelationKind.Friend, null)),
            Ev(Low, new ContactRelationAdded(Low, other, ContactRelationKind.Parent, "father")),
            Ev(Low, new ContactRelationRemoved(Low, other, ContactRelationKind.Friend)),
            Ev(Low, new ContactRelationEnded(Low, other, ContactRelationKind.Parent, new DateOnly(2024, 6, 1))),
            Ev(High, new ContactRelationAdded(High, other, ContactRelationKind.Friend, null)),
            Ev(High, new ContactRelationsReplaced(High, [new ContactRelation { ToContactId = other, Kind = ContactRelationKind.Sibling }])),
        ]);

        var low = Assert.Single(copies[Low]);
        Assert.Equal((ContactRelationKind.Parent, "father", true, new DateOnly(2024, 6, 1)), (low.Kind, low.Label, low.Ended, low.Until));
        Assert.Equal(ContactRelationKind.Sibling, Assert.Single(copies[High]).Kind);
    }

    [Fact]
    public void One_copy_becomes_the_relationship_whichever_side_held_it()
    {
        var onChild = Migrated(Ev(Low, new ContactRelationAdded(Low, High, ContactRelationKind.Parent, "dad", new DateOnly(1990, 1, 1))));
        var onParent = Migrated(Ev(High, new ContactRelationAdded(High, Low, ContactRelationKind.Child, null, new DateOnly(1990, 1, 1))));

        Assert.Equal(onChild.Id, onParent.Id);
        Assert.Equal((Low, High, ContactRelationKind.Parent), (onParent.Low, onParent.High, onParent.Kind));
        Assert.Equal(("dad", null), (onChild.LabelFromLow, onChild.LabelFromHigh));
        Assert.Equal(new DateOnly(1990, 1, 1), onParent.Since);
    }

    [Fact]
    public void Two_copies_merge_with_a_label_per_side()
    {
        var r = Migrated(
            Ev(Low, new ContactRelationAdded(Low, High, ContactRelationKind.Parent, "dad")),
            Ev(High, new ContactRelationAdded(High, Low, ContactRelationKind.Child, "son")));

        Assert.Equal(("dad", "son"), (r.LabelFromLow, r.LabelFromHigh));
    }

    [Fact]
    public void Shared_fields_come_from_the_low_copy_first()
    {
        var r = Migrated(
            Ev(High, new ContactRelationAdded(High, Low, ContactRelationKind.Friend, null, new DateOnly(2001, 1, 1), "school")),
            Ev(Low, new ContactRelationAdded(Low, High, ContactRelationKind.Friend, null, new DateOnly(2002, 2, 2))));

        Assert.Equal((new DateOnly(2002, 2, 2), "school"), (r.Since, r.Note));
    }

    [Fact]
    public void Ended_only_when_every_copy_was()
    {
        var oneLive = Migrated(
            Ev(Low, new ContactRelationAdded(Low, High, ContactRelationKind.Spouse, null)),
            Ev(Low, new ContactRelationEnded(Low, High, ContactRelationKind.Spouse, new DateOnly(2020, 5, 1))),
            Ev(High, new ContactRelationAdded(High, Low, ContactRelationKind.Spouse, null)));
        Assert.False(oneLive.Ended);

        var bothEnded = Migrated(
            Ev(Low, new ContactRelationAdded(Low, High, ContactRelationKind.Spouse, null)),
            Ev(Low, new ContactRelationEnded(Low, High, ContactRelationKind.Spouse, new DateOnly(2020, 5, 1))),
            Ev(High, new ContactRelationAdded(High, Low, ContactRelationKind.Spouse, null)),
            Ev(High, new ContactRelationEnded(High, Low, ContactRelationKind.Spouse, null)));
        Assert.Equal((true, new DateOnly(2020, 5, 1)), (bothEnded.Ended, bothEnded.Until));
    }

    [Fact]
    public void Self_loops_and_distinct_kinds_are_handled()
    {
        var copies = LegacyRelationFold.Copies(
        [
            Ev(Low, new ContactRelationAdded(Low, Low, ContactRelationKind.Friend, null)),
            Ev(Low, new ContactRelationAdded(Low, High, ContactRelationKind.Friend, null)),
            Ev(High, new ContactRelationAdded(High, Low, ContactRelationKind.Colleague, null)),
        ]);

        var kinds = LegacyRelationFold.Relationships(copies)
            .Select(x => ((RelationshipEstablished)x.Events[0]).Kind).Order();
        Assert.Equal([ContactRelationKind.Friend, ContactRelationKind.Colleague], kinds);
    }
}
