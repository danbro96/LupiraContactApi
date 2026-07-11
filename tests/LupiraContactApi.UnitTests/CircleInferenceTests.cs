using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Pure circle derivation around a focus contact: explicit edges, kinship closure, shared organizations,
/// and shared home places — ended edges excluded, one entry per contact per circle, multi-circle membership allowed.</summary>
public class CircleInferenceTests
{
    // Focus F: spouse S (ended: EX), parent P, P's parent G (grandparent), P's other child B (inferred sibling),
    // friend FR (also a colleague via org), colleague CO (explicit), neighbor N (no circle),
    // household H sharing F's home place, W sharing only a Work place.
    static readonly Guid F = new("11111111-0000-0000-0000-000000000001");
    static readonly Guid S = new("11111111-0000-0000-0000-000000000002");
    static readonly Guid EX = new("11111111-0000-0000-0000-000000000003");
    static readonly Guid P = new("11111111-0000-0000-0000-000000000004");
    static readonly Guid G = new("11111111-0000-0000-0000-000000000005");
    static readonly Guid B = new("11111111-0000-0000-0000-000000000006");
    static readonly Guid FR = new("11111111-0000-0000-0000-000000000007");
    static readonly Guid CO = new("11111111-0000-0000-0000-000000000008");
    static readonly Guid N = new("11111111-0000-0000-0000-000000000009");
    static readonly Guid H = new("11111111-0000-0000-0000-00000000000a");
    static readonly Guid W = new("11111111-0000-0000-0000-00000000000b");
    static readonly Guid HomePlace = new("22222222-0000-0000-0000-000000000001");
    static readonly Guid WorkPlace = new("22222222-0000-0000-0000-000000000002");

    static Contact Person(Guid id, params ContactRelation[] rels) => new() { Id = id, Relations = [.. rels] };
    static ContactRelation Edge(Guid to, ContactRelationKind kind, bool ended = false) => new() { ToContactId = to, Kind = kind, Ended = ended };
    static ContactPostalAddress At(Guid place, ContactAddressType type) => new() { PlaceId = place, Type = type };

    static List<Contact> World()
    {
        var f = Person(F, Edge(S, ContactRelationKind.Spouse), Edge(EX, ContactRelationKind.Partner, ended: true),
            Edge(P, ContactRelationKind.Parent), Edge(FR, ContactRelationKind.Friend), Edge(N, ContactRelationKind.Neighbor));
        f.Addresses = [At(HomePlace, ContactAddressType.Home), At(WorkPlace, ContactAddressType.Work)];
        var h = Person(H);
        h.Addresses = [At(HomePlace, ContactAddressType.Home)];
        var w = Person(W);
        w.Addresses = [At(WorkPlace, ContactAddressType.Home)];   // shares only F's WORK place — must not match
        var co = Person(CO, Edge(F, ContactRelationKind.Colleague));   // incoming edge
        return [f, Person(S), Person(EX), Person(P, Edge(G, ContactRelationKind.Parent), Edge(B, ContactRelationKind.Child)),
            Person(G), Person(B), Person(FR), co, Person(N), h, w];
    }

    static ContactGroup Org(params Guid[] members) => new()
    {
        Id = Guid.NewGuid(),
        Kind = ContactGroupKind.Organization,
        Name = "Acme",
        Members = [.. members.Select(id => new GroupMembership { ContactId = id })],
    };

    static ILookup<CircleKind, CircleMembership> Infer(IReadOnlyCollection<ContactGroup>? orgs = null) =>
        CircleInference.Infer(F, World(), orgs ?? []).ToLookup(m => m.Circle);

    [Fact]
    public void Close_family_holds_spouse_parent_and_inferred_sibling_but_not_the_ex()
    {
        var close = Infer()[CircleKind.CloseFamily].ToDictionary(m => m.ContactId);
        Assert.Equal(ContactRelationKind.Spouse, close[S].Kind);
        Assert.Equal(RelationProvenance.Explicit, close[S].Provenance);
        Assert.Equal(ContactRelationKind.Parent, close[P].Kind);
        Assert.Equal(ContactRelationKind.Sibling, close[B].Kind);
        Assert.Equal(RelationProvenance.Inferred, close[B].Provenance);
        Assert.All(close.Values, m => Assert.Equal(1, m.Degree));
        Assert.False(close.ContainsKey(EX));   // ended edges assert no current relationship
    }

    [Fact]
    public void Extended_family_holds_the_grandparent_at_degree_two()
    {
        var extended = Infer()[CircleKind.ExtendedFamily].ToDictionary(m => m.ContactId);
        Assert.Equal(ContactRelationKind.Grandparent, extended[G].Kind);
        Assert.Equal(2, extended[G].Degree);
        Assert.Equal(RelationProvenance.Inferred, extended[G].Provenance);
    }

    [Fact]
    public void Friends_and_explicit_colleagues_resolve_from_either_edge_direction()
    {
        var circles = Infer();
        Assert.Equal(FR, Assert.Single(circles[CircleKind.Friends]).ContactId);
        var colleague = Assert.Single(circles[CircleKind.Colleagues]);
        Assert.Equal(CO, colleague.ContactId);   // stored on CO's side, resolved via the inverse
        Assert.Equal(RelationProvenance.Explicit, colleague.Provenance);
    }

    [Fact]
    public void A_shared_organization_infers_colleagues_and_a_friend_can_sit_in_both_circles()
    {
        var circles = Infer([Org(F, FR)]);
        Assert.Contains(circles[CircleKind.Friends], m => m.ContactId == FR);
        var viaOrg = Assert.Single(circles[CircleKind.Colleagues], m => m.ContactId == FR);
        Assert.Equal(RelationProvenance.Inferred, viaOrg.Provenance);
    }

    [Fact]
    public void An_explicit_colleague_edge_wins_over_org_co_membership()
    {
        var only = Assert.Single(Infer([Org(F, CO)])[CircleKind.Colleagues], m => m.ContactId == CO);
        Assert.Equal(RelationProvenance.Explicit, only.Provenance);   // one entry per contact per circle, explicit first
    }

    [Fact]
    public void Household_matches_a_shared_home_place_only()
    {
        var household = Infer()[CircleKind.Household].ToList();
        var member = Assert.Single(household);
        Assert.Equal(H, member.ContactId);
        Assert.Null(member.Kind);   // co-residency makes no kinship claim
        // W shares F's work place (and even calls it Home on its side) — F's Home set never contained it.
    }

    [Fact]
    public void Explicit_grandparent_edge_with_no_chain_lands_in_extended_family()
    {
        // The linking parent isn't a contact, so inference can't derive it — the stored edge must still place G.
        var focus = Guid.NewGuid();
        var grandpa = Guid.NewGuid();
        var contacts = new List<Contact> { Person(focus, Edge(grandpa, ContactRelationKind.Grandparent)), Person(grandpa) };
        var member = Assert.Single(CircleInference.Infer(focus, contacts, []), m => m.Circle == CircleKind.ExtendedFamily);
        Assert.Equal(grandpa, member.ContactId);
        Assert.Equal(ContactRelationKind.Grandparent, member.Kind);
        Assert.Equal(2, member.Degree);
        Assert.Equal(RelationProvenance.Explicit, member.Provenance);
    }

    [Fact]
    public void Neighbors_and_the_focus_itself_join_no_circle()
    {
        var all = CircleInference.Infer(F, World(), []);
        Assert.DoesNotContain(all, m => m.ContactId == N);
        Assert.DoesNotContain(all, m => m.ContactId == F);
    }

    [Fact]
    public void Unknown_focus_yields_nothing() =>
        Assert.Empty(CircleInference.Infer(Guid.NewGuid(), World(), []));
}
