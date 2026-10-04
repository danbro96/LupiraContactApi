using LupiraContactApi.Core.Domain.Inference;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Pure kinship derivation over in-memory relationships — parent/child stated from either side,
/// two-generation closure, explicit-relationship precedence.</summary>
public class KinshipInferenceTests
{
    // A three-generation family with parentage stated from mixed sides:
    //   A --Parent--> P,  P --Child--> B,  P --Parent--> G,  G --Child--> U,  U --Child--> C
    // So: G is grandparent of A/B; P & U are G's children (siblings); A & B are P's children; C is U's child.
    private static readonly Guid G = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid U = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid A = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid B = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid C = new("66666666-6666-6666-6666-666666666666");

    private static Relationship Rel(Guid self, Guid other, ContactRelationKind kind, bool ended = false) => TestRelationships.Of(self, other, kind, ended);

    private static List<Relationship> Family() =>
    [
        Rel(G, U, ContactRelationKind.Child),
        Rel(P, G, ContactRelationKind.Parent),
        Rel(P, B, ContactRelationKind.Child),
        Rel(U, C, ContactRelationKind.Child),
        Rel(A, P, ContactRelationKind.Parent),
    ];

    // Everyone named in the relationships is known (readable).
    private static Dictionary<Guid, ContactRelationKind> Infer(Guid focus, IReadOnlyCollection<Relationship> relationships) =>
        KinshipInference.Infer(focus, relationships.SelectMany(r => new[] { r.Low, r.High }).ToHashSet(), relationships)
            .ToDictionary(k => k.ContactId, k => k.Kind);

    [Fact]
    public void Infers_the_two_generation_closure_around_a_child()
    {
        var kin = Infer(A, Family());
        Assert.Equal(ContactRelationKind.Sibling, kin[B]);
        Assert.Equal(ContactRelationKind.Grandparent, kin[G]);
        Assert.Equal(ContactRelationKind.AuntUncle, kin[U]);
        Assert.Equal(ContactRelationKind.Cousin, kin[C]);
        Assert.False(kin.ContainsKey(P));   // P is an explicit parent, surfaced separately
        Assert.False(kin.ContainsKey(A));
    }

    [Fact]
    public void Infers_grandchildren_from_the_top()
    {
        var kin = Infer(G, Family());
        Assert.Equal(ContactRelationKind.Grandchild, kin[A]);
        Assert.Equal(ContactRelationKind.Grandchild, kin[B]);
        Assert.Equal(ContactRelationKind.Grandchild, kin[C]);
        Assert.False(kin.ContainsKey(P));   // explicit child, stated from P
        Assert.False(kin.ContainsKey(U));   // explicit child, stated from G
    }

    [Fact]
    public void Infers_nieces_and_nephews_for_an_uncle()
    {
        var kin = Infer(U, Family());
        Assert.Equal(ContactRelationKind.Sibling, kin[P]);       // co-child of G
        Assert.Equal(ContactRelationKind.NieceNephew, kin[A]);   // child of sibling P
        Assert.Equal(ContactRelationKind.NieceNephew, kin[B]);
    }

    [Fact]
    public void Derives_siblings_from_a_shared_parent_whichever_side_stated_it()
    {
        // X's parentage was stated from X; Y's from the parent, as Child. Still siblings.
        var parent = Guid.NewGuid();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        List<Relationship> relationships = [Rel(x, parent, ContactRelationKind.Parent), Rel(parent, y, ContactRelationKind.Child)];
        Assert.Equal(ContactRelationKind.Sibling, Infer(x, relationships)[y]);
        Assert.Equal(ContactRelationKind.Sibling, Infer(y, relationships)[x]);
    }

    [Fact]
    public void Explicit_edges_win_over_inferred_kinship()
    {
        var family = Family();
        // Pin an explicit Friend relationship A–C; C must not also surface as an inferred cousin.
        family.Add(Rel(A, C, ContactRelationKind.Friend));
        Assert.False(Infer(A, family).ContainsKey(C));
    }

    [Fact]
    public void Explicit_sibling_partner_is_excluded_from_inferred_results()
    {
        // An explicit Sibling relationship is listed as explicit, so inference omits it — that exclusion is what lets
        // explicit relationships and shared-parent inference coexist without double-listing.
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        Assert.False(Infer(x, [Rel(x, y, ContactRelationKind.Sibling)]).ContainsKey(y));
    }

    [Fact]
    public void Ended_edges_are_excluded_from_the_kinship_graph()
    {
        // X's parent relationship is ended (estrangement modeling aside, the graph must not assert it) — no sibling inference via it.
        var parent = Guid.NewGuid();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        List<Relationship> relationships = [Rel(x, parent, ContactRelationKind.Parent, ended: true), Rel(parent, y, ContactRelationKind.Child)];
        Assert.False(Infer(x, relationships).ContainsKey(y));
    }

    [Fact]
    public void Parent_cycle_is_detected_directly_and_transitively()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        List<Relationship> relationships =
        [
            Rel(a, b, ContactRelationKind.Parent),   // b is a's parent
            Rel(b, c, ContactRelationKind.Parent),   // c is b's parent
        ];
        Assert.True(KinshipInference.WouldCreateParentCycle(a, a, relationships));    // self
        Assert.True(KinshipInference.WouldCreateParentCycle(b, a, relationships));    // direct: a is b's child
        Assert.True(KinshipInference.WouldCreateParentCycle(c, a, relationships));    // transitive: a → b → c
        Assert.False(KinshipInference.WouldCreateParentCycle(a, c, relationships));   // c is already a's ancestor — no new cycle
    }

    [Fact]
    public void Cycle_check_terminates_on_pre_existing_bad_data()
    {
        // a and b are each other's parents (imported bad data) — the visited set must stop the walk.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var other = Guid.NewGuid();
        List<Relationship> relationships = [Rel(a, b, ContactRelationKind.Parent), Rel(b, a, ContactRelationKind.Parent)];
        Assert.False(KinshipInference.WouldCreateParentCycle(other, a, relationships));
    }
}
