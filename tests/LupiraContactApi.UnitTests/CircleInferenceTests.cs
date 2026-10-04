using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Inference;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Pure circle derivation around a focus contact: explicit relationships, kinship closure, shared organizations,
/// and shared home places — ended relationships excluded, one entry per contact per circle, multi-circle membership allowed.</summary>
public class CircleInferenceTests
{
    // Focus F: spouse S (ended: EX), parent P, P's parent G (grandparent), P's other child B (inferred sibling),
    // friend FR (also a colleague via org), colleague CO (explicit), neighbor N (no circle),
    // household H sharing F's home place, W sharing only a Work place.
    private static readonly Guid F = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid S = new("11111111-0000-0000-0000-000000000002");
    private static readonly Guid EX = new("11111111-0000-0000-0000-000000000003");
    private static readonly Guid P = new("11111111-0000-0000-0000-000000000004");
    private static readonly Guid G = new("11111111-0000-0000-0000-000000000005");
    private static readonly Guid B = new("11111111-0000-0000-0000-000000000006");
    private static readonly Guid FR = new("11111111-0000-0000-0000-000000000007");
    private static readonly Guid CO = new("11111111-0000-0000-0000-000000000008");
    private static readonly Guid N = new("11111111-0000-0000-0000-000000000009");
    private static readonly Guid H = new("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid W = new("11111111-0000-0000-0000-00000000000b");
    private static readonly Guid HomePlace = new("22222222-0000-0000-0000-000000000001");
    private static readonly Guid WorkPlace = new("22222222-0000-0000-0000-000000000002");

    private static Contact Person(Guid id) => new() { Id = id };

    private static Relationship Rel(Guid self, Guid other, ContactRelationKind kind, bool ended = false) => TestRelationships.Of(self, other, kind, ended);

    private static Residency At(Guid contact, Guid place, ContactAddressType type, FuzzyDate? movedIn = null, FuzzyDate? movedOut = null) =>
        new() { Id = Guid.NewGuid(), ContactId = contact, PlaceId = place, Type = type, MovedIn = movedIn, MovedOut = movedOut };

    private static List<Contact> World() =>
        [Person(F), Person(S), Person(EX), Person(P), Person(G), Person(B), Person(FR), Person(CO), Person(N), Person(H), Person(W)];

    private static List<Residency> Residencies() =>
    [
        At(F, HomePlace, ContactAddressType.Home),
        At(F, WorkPlace, ContactAddressType.Work),
        At(H, HomePlace, ContactAddressType.Home),
        At(W, WorkPlace, ContactAddressType.Home),   // shares only F's WORK place — must not match
    ];

    private static List<Relationship> Relationships() =>
    [
        Rel(F, S, ContactRelationKind.Spouse),
        Rel(F, EX, ContactRelationKind.Partner, ended: true),
        Rel(F, P, ContactRelationKind.Parent),
        Rel(F, FR, ContactRelationKind.Friend),
        Rel(F, N, ContactRelationKind.Neighbor),
        Rel(P, G, ContactRelationKind.Parent),
        Rel(P, B, ContactRelationKind.Child),
        Rel(CO, F, ContactRelationKind.Colleague),   // stated from the other side
    ];

    private static ContactGroup Org(params Guid[] members) => new()
    {
        Id = Guid.NewGuid(),
        Kind = ContactGroupKind.Organization,
        Name = "Acme",
        Members = [.. members.Select(id => new GroupMembership { ContactId = id })],
    };

    private static ILookup<CircleKind, CircleMembership> Infer(IReadOnlyCollection<ContactGroup>? orgs = null) =>
        CircleInference.Infer(F, World(), Relationships(), Residencies(), orgs ?? []).ToLookup(m => m.Circle);

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
    public void A_shared_vacation_home_is_not_a_household()
    {
        List<Residency> residencies = [.. Residencies(), At(F, WorkPlace, ContactAddressType.Vacation), At(N, WorkPlace, ContactAddressType.Vacation)];
        var household = CircleInference.Infer(F, World(), Relationships(), residencies, []).ToLookup(m => m.Circle)[CircleKind.Household];
        Assert.Equal([H], household.Select(m => m.ContactId));
    }

    [Fact]
    public void Former_home_address_does_not_infer_household()
    {
        // EX used to live at F's home place but moved out — a former residency asserts no current cohabitation.
        List<Residency> residencies = [.. Residencies(), At(EX, HomePlace, ContactAddressType.Home, movedOut: new FuzzyDate(2015))];
        var household = CircleInference.Infer(F, World(), Relationships(), residencies, [], today: new DateOnly(2026, 8, 16)).ToLookup(m => m.Circle)[CircleKind.Household].ToList();
        var member = Assert.Single(household);
        Assert.Equal(H, member.ContactId);
    }

    [Fact]
    public void Future_move_out_still_cohabits_future_move_in_does_not()
    {
        var today = new DateOnly(2026, 8, 16);
        List<Residency> residencies =
        [
            .. Residencies().Where(r => r.ContactId != H),
            At(H, HomePlace, ContactAddressType.Home, movedOut: new FuzzyDate(2027, 3)),   // a planned move-out: still home today
            At(EX, HomePlace, ContactAddressType.Home, movedIn: new FuzzyDate(2027)),      // moves in next year: not yet
        ];

        var household = CircleInference.Infer(F, World(), Relationships(), residencies, [], today).ToLookup(m => m.Circle)[CircleKind.Household].ToList();
        var member = Assert.Single(household);
        Assert.Equal(H, member.ContactId);
    }

    [Fact]
    public void Explicit_grandparent_edge_with_no_chain_lands_in_extended_family()
    {
        // The linking parent isn't a contact, so inference can't derive it — the stored relationship must still place G.
        var focus = Guid.NewGuid();
        var grandpa = Guid.NewGuid();
        var member = Assert.Single(
            CircleInference.Infer(focus, [Person(focus), Person(grandpa)], [Rel(focus, grandpa, ContactRelationKind.Grandparent)], [], []),
            m => m.Circle == CircleKind.ExtendedFamily);
        Assert.Equal(grandpa, member.ContactId);
        Assert.Equal(ContactRelationKind.Grandparent, member.Kind);
        Assert.Equal(2, member.Degree);
        Assert.Equal(RelationProvenance.Explicit, member.Provenance);
    }

    [Fact]
    public void Neighbors_and_the_focus_itself_join_no_circle()
    {
        var all = CircleInference.Infer(F, World(), Relationships(), Residencies(), []);
        Assert.DoesNotContain(all, m => m.ContactId == N);
        Assert.DoesNotContain(all, m => m.ContactId == F);
    }

    [Fact]
    public void Unknown_focus_yields_nothing() =>
        Assert.Empty(CircleInference.Infer(Guid.NewGuid(), World(), Relationships(), Residencies(), []));
}
