using System.Net;
using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The kinship rules: explicit Sibling edges are stored as-is (never fabricated into parentage), siblinghood is
/// also derived from shared parents, extended kinds are storable when the linking relative isn't a contact, and the
/// two-generation inferred read surface is opt-in.</summary>
public sealed class ContactKinshipTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task AddRelationAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind) =>
        (await api.PostAsJsonAsync($"/contacts/{contactId}/relations", new AddContactRelationRequest { ToContactId = toContactId, Kind = kind })).EnsureSuccessStatusCode();

    static async Task<List<ContactRelationEntryDto>> RelationsAsync(HttpClient api, Guid id, bool inferred = false) =>
        (await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{id}/relations?includeInferred={inferred}"))!;

    [Fact]
    public async Task Sibling_with_a_known_parent_is_stored_as_an_explicit_edge_not_fabricated_parentage()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");
        var child = await CreateContactAsync(api, ab, "Cara", "Child");
        var sib = await CreateContactAsync(api, ab, "Sam", "Sibling");

        await AddRelationAsync(api, child.Id, parent.Id, ContactRelationKind.Parent);   // child's parent is known
        await AddRelationAsync(api, child.Id, sib.Id, ContactRelationKind.Sibling);

        // The explicit Sibling relationship is stored as-is (listed explicitly, not inferred) and the sibling is NOT
        // given a fabricated parent.
        Assert.Contains(await RelationsAsync(api, child.Id),
            e => e.ContactId == sib.Id && e.Kind == ContactRelationKind.Sibling && e.Provenance == RelationProvenance.Explicit);
        Assert.DoesNotContain(await RelationsAsync(api, sib.Id), e => e.ContactId == parent.Id);
    }

    [Fact]
    public async Task Adding_a_parent_keeps_the_explicit_sibling_edge()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, ab, "Ann", "A");
        var b = await CreateContactAsync(api, ab, "Bo", "B");
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");

        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Sibling);
        await AddRelationAsync(api, a.Id, parent.Id, ContactRelationKind.Parent);

        // The explicit Sibling relationship survives; B does not inherit A's parent.
        Assert.Contains(await RelationsAsync(api, a.Id), e => e.ContactId == b.Id && e.Kind == ContactRelationKind.Sibling);
        Assert.DoesNotContain(await RelationsAsync(api, b.Id), e => e.ContactId == parent.Id);
    }

    [Fact]
    public async Task Shared_parent_still_infers_siblinghood_without_an_explicit_edge()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");
        var a = await CreateContactAsync(api, ab, "Ann", "A");
        var b = await CreateContactAsync(api, ab, "Bo", "B");

        await AddRelationAsync(api, a.Id, parent.Id, ContactRelationKind.Parent);
        await AddRelationAsync(api, b.Id, parent.Id, ContactRelationKind.Parent);

        Assert.Contains(await RelationsAsync(api, a.Id, inferred: true),
            e => e.ContactId == b.Id && e.Kind == ContactRelationKind.Sibling && e.Provenance == RelationProvenance.Inferred);
    }

    [Fact]
    public async Task Extended_kind_is_storable_when_the_linking_relative_is_absent()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var focus = await CreateContactAsync(api, ab, "Ann", "Focus");
        var grandma = await CreateContactAsync(api, ab, "Grace", "Gran");

        await AddRelationAsync(api, focus.Id, grandma.Id, ContactRelationKind.Grandparent);

        // The grandmother sees the same relationship as Grandchild.
        Assert.Contains(await RelationsAsync(api, focus.Id), e => e.ContactId == grandma.Id && e.Kind == ContactRelationKind.Grandparent);
        Assert.Contains(await RelationsAsync(api, grandma.Id), e => e.ContactId == focus.Id && e.Kind == ContactRelationKind.Grandchild);

        // And it survives a sync-adapter GET → PUT round-trip.
        var vcf = await api.GetStringAsync($"/dav-backend/u/{Uri.EscapeDataString(Email)}/collections/{ab}/resources/{focus.ExternalId}");
        Assert.Contains($"RELATED;TYPE=grandparent:urn:uuid:{grandma.Id:D}", vcf);
        (await PutVcfAsync(api, Email, ab, focus.ExternalId, vcf)).EnsureSuccessStatusCode();
        Assert.Contains(await RelationsAsync(api, focus.Id), e => e.ContactId == grandma.Id && e.Kind == ContactRelationKind.Grandparent);
    }

    [Fact]
    public async Task Inferred_listing_returns_the_two_generation_closure_only_when_requested()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var gp = await CreateContactAsync(api, ab, "Grand", "Pa");
        var p = await CreateContactAsync(api, ab, "Pat", "Parent");
        var unc = await CreateContactAsync(api, ab, "Uncle", "Bob");
        var a = await CreateContactAsync(api, ab, "Ann", "Focus");
        var cous = await CreateContactAsync(api, ab, "Cory", "Cousin");

        await AddRelationAsync(api, p.Id, gp.Id, ContactRelationKind.Parent);     // p, unc are gp's children (siblings)
        await AddRelationAsync(api, unc.Id, gp.Id, ContactRelationKind.Parent);
        await AddRelationAsync(api, a.Id, p.Id, ContactRelationKind.Parent);       // a is p's child
        await AddRelationAsync(api, cous.Id, unc.Id, ContactRelationKind.Parent);  // cousin is unc's child

        var explicitOnly = await RelationsAsync(api, a.Id);
        Assert.Equal(p.Id, Assert.Single(explicitOnly).ContactId);   // only the explicit parent
        Assert.All(explicitOnly, e => Assert.Equal(RelationProvenance.Explicit, e.Provenance));

        var inferred = await RelationsAsync(api, a.Id, inferred: true);
        ContactRelationKind Kind(Guid id) => inferred.Single(e => e.ContactId == id).Kind;
        Assert.Equal(ContactRelationKind.Grandparent, Kind(gp.Id));
        Assert.Equal(ContactRelationKind.AuntUncle, Kind(unc.Id));
        Assert.Equal(ContactRelationKind.Cousin, Kind(cous.Id));
        Assert.All(inferred.Where(e => e.ContactId != p.Id), e => Assert.Equal(RelationProvenance.Inferred, e.Provenance));
    }
}
