using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Merging per-side copies into one relationship: the same view whichever side holds it, per-side labels,
/// shared fields that agree across viewers, and the side-independent key.</summary>
public class RelationResolverTests
{
    private static readonly Guid Low = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid High = new("99999999-9999-9999-9999-999999999999");

    private static RelationCopy Copy(Guid holder, Guid to, ContactRelationKind kind, string? label = null, DateOnly? since = null, string? note = null, bool ended = false, DateOnly? until = null) =>
        new(holder, new ContactRelation { ToContactId = to, Kind = kind, Label = label, Since = since, Note = note, Ended = ended, Until = until });

    [Fact]
    public void A_copy_reads_the_same_from_both_sides_whichever_holds_it()
    {
        var onChild = Copy(Low, High, ContactRelationKind.Parent, since: new DateOnly(1990, 1, 1));
        var onParent = Copy(High, Low, ContactRelationKind.Child, since: new DateOnly(1990, 1, 1));

        foreach (var copy in new[] { onChild, onParent })
        {
            var fromChild = Assert.Single(RelationResolver.Resolve(Low, [copy]));
            var fromParent = Assert.Single(RelationResolver.Resolve(High, [copy]));
            Assert.Equal(new ResolvedRelation(High, ContactRelationKind.Parent, null, new DateOnly(1990, 1, 1), null, false, null), fromChild);
            Assert.Equal(new ResolvedRelation(Low, ContactRelationKind.Child, null, new DateOnly(1990, 1, 1), null, false, null), fromParent);
        }
    }

    [Fact]
    public void Both_copies_merge_into_one_relationship_with_a_label_per_side()
    {
        Guid[] viewers = [Low, High];
        var copies = new[]
        {
            Copy(Low, High, ContactRelationKind.Parent, label: "dad"),
            Copy(High, Low, ContactRelationKind.Child, label: "son"),
        };

        var labels = viewers.Select(v => Assert.Single(RelationResolver.Resolve(v, copies)).Label);
        Assert.Equal(["dad", "son"], labels);
    }

    [Fact]
    public void Shared_fields_agree_across_viewers_even_when_legacy_copies_disagree()
    {
        var copies = new[]
        {
            Copy(High, Low, ContactRelationKind.Friend, since: new DateOnly(2001, 1, 1), note: "school"),
            Copy(Low, High, ContactRelationKind.Friend, since: new DateOnly(2002, 2, 2)),
        };

        var fromLow = Assert.Single(RelationResolver.Resolve(Low, copies));
        var fromHigh = Assert.Single(RelationResolver.Resolve(High, copies));
        Assert.Equal((new DateOnly(2002, 2, 2), "school"), (fromLow.Since, fromLow.Note));   // Low's copy first, the other fills gaps
        Assert.Equal((fromLow.Since, fromLow.Note), (fromHigh.Since, fromHigh.Note));
    }

    [Fact]
    public void Ended_only_when_every_copy_is()
    {
        var until = new DateOnly(2020, 5, 1);
        var oneLive = new[] { Copy(Low, High, ContactRelationKind.Spouse, ended: true, until: until), Copy(High, Low, ContactRelationKind.Spouse) };
        var bothEnded = new[] { Copy(Low, High, ContactRelationKind.Spouse, ended: true, until: until), Copy(High, Low, ContactRelationKind.Spouse, ended: true) };

        var live = Assert.Single(RelationResolver.Resolve(Low, oneLive));
        Assert.False(live.Ended);
        Assert.Null(live.Until);

        var ended = Assert.Single(RelationResolver.Resolve(High, bothEnded));
        Assert.True(ended.Ended);
        Assert.Equal(until, ended.Until);
    }

    [Fact]
    public void Distinct_kinds_between_the_same_pair_stay_distinct()
    {
        var copies = new[] { Copy(Low, High, ContactRelationKind.Friend), Copy(High, Low, ContactRelationKind.Colleague) };

        var kinds = RelationResolver.Resolve(Low, copies).Select(v => v.Kind).Order();
        Assert.Equal([ContactRelationKind.Friend, ContactRelationKind.Colleague], kinds);
    }

    [Fact]
    public void Copies_not_touching_the_viewer_are_ignored()
    {
        var bystander = Guid.NewGuid();
        var copies = new[] { Copy(High, bystander, ContactRelationKind.Friend), Copy(Low, Low, ContactRelationKind.Friend) };

        Assert.Empty(RelationResolver.Resolve(Low, copies));
    }

    [Fact]
    public void Key_is_the_same_from_either_side_and_orders_by_the_ids_string_form()
    {
        Assert.Equal(RelationshipKey.Of(Low, High, ContactRelationKind.Parent), RelationshipKey.Of(High, Low, ContactRelationKind.Child));
        Assert.Equal(new RelationshipKey(Low, High, ContactRelationKind.Parent), RelationshipKey.Of(High, Low, ContactRelationKind.Child));

        var a = new Guid("80000000-0000-0000-0000-000000000000");
        var b = new Guid("0fffffff-ffff-ffff-ffff-ffffffffffff");
        Assert.Equal(b, RelationshipKey.Of(a, b, ContactRelationKind.Friend).Low);
    }
}
