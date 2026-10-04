using JasperFx.Events;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Relationships.Events;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The relationship aggregate: one record whichever contact states it, a label per side, the rest shared, and
/// decisions that append nothing when the stated state already holds.</summary>
public class RelationshipTests
{
    private static readonly Guid Low = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid High = new("99999999-9999-9999-9999-999999999999");
    private static long _seq;

    private static Relationship Fold(IEnumerable<object> events)
    {
        var r = new Relationship();
        foreach (var data in events) Apply(r, data);
        return r;
    }

    private static void Apply(Relationship r, object data)
    {
        switch (data)
        {
            case RelationshipEstablished d: r.Apply(Ev(d)); break;
            case RelationshipRevised d: r.Apply(Ev(d)); break;
            case RelationshipLabelled d: r.Apply(Ev(d)); break;
            case RelationshipEnded d: r.Apply(Ev(d)); break;
            case RelationshipRevived d: r.Apply(Ev(d)); break;
            case RelationshipRemoved d: r.Apply(Ev(d)); break;
        }
    }

    private static IEvent<T> Ev<T>(T data)
        where T : notnull
    {
        var e = Event.For(data);
        e.Sequence = Interlocked.Increment(ref _seq);
        return e;
    }

    private static Relationship Stated(Guid by, Guid other, ContactRelationKind kind, string? label = null, DateOnly? since = null) =>
        Fold(Relationship.Upsert(null, RelationshipKey.Of(by, other, kind), by, label, since, null));

    [Fact]
    public void Stating_from_either_side_lands_on_the_same_record()
    {
        var fromChild = Stated(Low, High, ContactRelationKind.Parent);
        var fromParent = Stated(High, Low, ContactRelationKind.Child);

        Assert.Equal(fromChild.Id, fromParent.Id);
        Assert.Equal((Low, High, ContactRelationKind.Parent), (fromParent.Low, fromParent.High, fromParent.Kind));
    }

    [Fact]
    public void Each_side_sees_the_other_with_its_own_label()
    {
        var r = Stated(Low, High, ContactRelationKind.Parent, "dad", new DateOnly(1990, 1, 1));
        Apply(r, new RelationshipLabelled(r.Id, High, "son"));

        Assert.Equal(new ResolvedRelation(High, ContactRelationKind.Parent, "dad", new DateOnly(1990, 1, 1), null, false, null), r.ViewFrom(Low));
        Assert.Equal(new ResolvedRelation(Low, ContactRelationKind.Child, "son", new DateOnly(1990, 1, 1), null, false, null), r.ViewFrom(High));
    }

    [Fact]
    public void Restating_what_holds_appends_nothing()
    {
        var r = Stated(Low, High, ContactRelationKind.Friend, "pal", new DateOnly(2001, 9, 1));
        Assert.Empty(Relationship.Upsert(r, r.Key, Low, "pal", new DateOnly(2001, 9, 1), null));
    }

    [Fact]
    public void Restating_from_the_other_side_keeps_this_sides_label()
    {
        var r = Stated(Low, High, ContactRelationKind.Friend, "pal");
        var events = Relationship.Upsert(r, r.Key, High, null, new DateOnly(2001, 9, 1), null);

        var revised = Assert.Single(events);
        Apply(r, revised);
        Assert.Equal(("pal", null, new DateOnly(2001, 9, 1)), (r.LabelFromLow, r.LabelFromHigh, r.Since));
    }

    [Fact]
    public void Restating_an_ended_relationship_revives_it()
    {
        var r = Stated(Low, High, ContactRelationKind.Spouse);
        foreach (var e in r.End(new DateOnly(2020, 5, 1))) Apply(r, e);
        Assert.True(r.ViewFrom(Low).Ended);

        foreach (var e in Relationship.Upsert(r, r.Key, High, null, null, null)) Apply(r, e);
        Assert.Equal((false, null), (r.Ended, r.ViewFrom(Low).Until));
    }

    [Fact]
    public void Ending_twice_with_the_same_date_appends_nothing()
    {
        var r = Stated(Low, High, ContactRelationKind.Spouse);
        foreach (var e in r.End(new DateOnly(2020, 5, 1))) Apply(r, e);
        Assert.Empty(r.End(new DateOnly(2020, 5, 1)));
        Assert.Single(r.End(null));
    }

    [Fact]
    public void Restating_a_removed_relationship_starts_it_afresh()
    {
        var r = Stated(Low, High, ContactRelationKind.Friend, "pal", new DateOnly(2001, 9, 1));
        foreach (var e in r.Remove()) Apply(r, e);
        Assert.False(r.IsLive);

        foreach (var e in Relationship.Upsert(r, r.Key, High, null, null, null)) Apply(r, e);
        Assert.True(r.IsLive);
        Assert.Equal((null, null, null), (r.LabelFromLow, r.LabelFromHigh, r.Since));
    }

    [Fact]
    public void Key_orders_by_the_ids_string_form()
    {
        var a = new Guid("80000000-0000-0000-0000-000000000000");
        var b = new Guid("0fffffff-ffff-ffff-ffff-ffffffffffff");
        Assert.Equal(new RelationshipKey(b, a, ContactRelationKind.Child), RelationshipKey.Of(a, b, ContactRelationKind.Parent));
    }
}
