using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Inference;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Pure kinship derivation over an in-memory contact set — parent/child edges read from either storage side,
/// two-generation closure, explicit-edge precedence.</summary>
public class KinshipInferenceTests
{
    // A three-generation family with parentage stored on mixed sides:
    //   A --Parent--> P,  P --Child--> B,  P --Parent--> G,  G --Child--> U,  U --Child--> C
    // So: G is grandparent of A/B; P & U are G's children (siblings); A & B are P's children; C is U's child.
    static readonly Guid G = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid P = new("22222222-2222-2222-2222-222222222222");
    static readonly Guid U = new("33333333-3333-3333-3333-333333333333");
    static readonly Guid A = new("44444444-4444-4444-4444-444444444444");
    static readonly Guid B = new("55555555-5555-5555-5555-555555555555");
    static readonly Guid C = new("66666666-6666-6666-6666-666666666666");

    static Contact Person(Guid id, params (Guid to, ContactRelationKind kind)[] rels) =>
        new() { Id = id, Relations = [.. rels.Select(r => new ContactRelation { ToContactId = r.to, Kind = r.kind })] };

    static List<Contact> Family() =>
    [
        Person(G, (U, ContactRelationKind.Child)),
        Person(P, (G, ContactRelationKind.Parent), (B, ContactRelationKind.Child)),
        Person(U, (C, ContactRelationKind.Child)),
        Person(A, (P, ContactRelationKind.Parent)),
        Person(B),
        Person(C),
    ];

    static Dictionary<Guid, ContactRelationKind> Infer(Guid focus, IReadOnlyCollection<Contact> contacts) =>
        KinshipInference.Infer(focus, contacts).ToDictionary(k => k.ContactId, k => k.Kind);

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
        Assert.False(kin.ContainsKey(P));   // explicit child (incoming Parent edge)
        Assert.False(kin.ContainsKey(U));   // explicit child (outgoing Child edge)
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
    public void Derives_siblings_from_a_shared_parent_regardless_of_storage_side()
    {
        // X stores its parent; Y's parentage is stored on the parent as a Child edge. Still siblings.
        var parent = Guid.NewGuid();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var contacts = new List<Contact>
        {
            Person(x, (parent, ContactRelationKind.Parent)),
            Person(parent, (y, ContactRelationKind.Child)),
            Person(y),
        };
        Assert.Equal(ContactRelationKind.Sibling, Infer(x, contacts)[y]);
        Assert.Equal(ContactRelationKind.Sibling, Infer(y, contacts)[x]);
    }

    [Fact]
    public void Explicit_edges_win_over_inferred_kinship()
    {
        var family = Family();
        // Pin an explicit Friend edge A→C; C must not also surface as an inferred cousin.
        family.Single(c => c.Id == A).Relations.Add(new ContactRelation { ToContactId = C, Kind = ContactRelationKind.Friend });
        Assert.False(Infer(A, family).ContainsKey(C));
    }

    [Fact]
    public void Explicit_sibling_partner_is_excluded_from_inferred_results()
    {
        // An explicit Sibling edge is surfaced as an explicit relation (ListRelationsAsync), so inference omits it —
        // that exclusion is what lets explicit edges and shared-parent inference coexist without double-listing.
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var contacts = new List<Contact>
        {
            Person(x, (y, ContactRelationKind.Sibling)),
            Person(y),
        };
        Assert.False(Infer(x, contacts).ContainsKey(y));
    }

    [Fact]
    public void Ended_edges_are_excluded_from_the_kinship_graph()
    {
        // X's parent edge is ended (estrangement modeling aside, the graph must not assert it) — no sibling inference via it.
        var parent = Guid.NewGuid();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var contacts = new List<Contact>
        {
            new() { Id = x, Relations = [new ContactRelation { ToContactId = parent, Kind = ContactRelationKind.Parent, Ended = true }] },
            Person(parent, (y, ContactRelationKind.Child)),
            Person(y),
        };
        Assert.False(Infer(x, contacts).ContainsKey(y));
    }

    [Fact]
    public void Parent_cycle_is_detected_directly_and_transitively()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var contacts = new List<Contact>
        {
            Person(a, (b, ContactRelationKind.Parent)),   // b is a's parent
            Person(b, (c, ContactRelationKind.Parent)),   // c is b's parent
            Person(c),
        };
        Assert.True(KinshipInference.WouldCreateParentCycle(a, a, contacts));    // self
        Assert.True(KinshipInference.WouldCreateParentCycle(b, a, contacts));    // direct: a is b's child
        Assert.True(KinshipInference.WouldCreateParentCycle(c, a, contacts));    // transitive: a → b → c
        Assert.False(KinshipInference.WouldCreateParentCycle(a, c, contacts));   // c is already a's ancestor — no new cycle
    }

    [Fact]
    public void Cycle_check_terminates_on_pre_existing_bad_data()
    {
        // a and b are each other's parents (imported bad data) — the visited set must stop the walk.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var other = Guid.NewGuid();
        var contacts = new List<Contact>
        {
            Person(a, (b, ContactRelationKind.Parent)),
            Person(b, (a, ContactRelationKind.Parent)),
            Person(other),
        };
        Assert.False(KinshipInference.WouldCreateParentCycle(other, a, contacts));
    }
}
