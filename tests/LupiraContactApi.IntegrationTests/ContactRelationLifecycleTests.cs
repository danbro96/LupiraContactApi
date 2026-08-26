using System.Net;
using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Relation lifecycle beyond add/remove: ending an edge (vs erasing a mistake), revival on re-add,
/// the parentage-cycle guard, and the ordered emergency-contact designation with its RELATED;TYPE=emergency round-trip.</summary>
public sealed class ContactRelationLifecycleTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task<ContactDto> AddRelationAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind)
    {
        var resp = await api.PostAsJsonAsync($"/contacts/{contactId}/relations", new AddContactRelationRequest { ToContactId = toContactId, Kind = kind });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    static async Task<HttpResponseMessage> EndRelationAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind, DateOnly? until = null) =>
        await api.PostAsJsonAsync($"/contacts/{contactId}/relations/{toContactId}/end", new EndContactRelationRequest { Kind = kind, Until = until });

    [Fact]
    public async Task Ended_relation_stays_listed_flagged_and_leaves_the_kinship_graph()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var focus = await CreateContactAsync(api, ab, "Ann", "Focus");
        var sib = await CreateContactAsync(api, ab, "Sam", "Sib");
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");

        await AddRelationAsync(api, focus.Id, parent.Id, ContactRelationKind.Parent);
        await AddRelationAsync(api, sib.Id, parent.Id, ContactRelationKind.Parent);
        var inferred = await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{focus.Id}/relations?includeInferred=true");
        Assert.Contains(inferred!, e => e.ContactId == sib.Id && e.Kind == ContactRelationKind.Sibling);

        var end = await EndRelationAsync(api, focus.Id, parent.Id, ContactRelationKind.Parent, new DateOnly(2024, 6, 1));
        end.EnsureSuccessStatusCode();
        var dto = (await end.Content.ReadFromJsonAsync<ContactDto>())!;
        var edge = Assert.Single(dto.Relations);
        Assert.True(edge.Ended);
        Assert.Equal(new DateOnly(2024, 6, 1), edge.Until);
        Assert.NotEqual(focus.Etag, dto.Etag);

        // Still listed (flagged), but no longer feeding inference.
        var listed = await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{focus.Id}/relations?includeInferred=true");
        Assert.Contains(listed!, e => e.ContactId == parent.Id && e.Ended && e.Until == new DateOnly(2024, 6, 1));
        Assert.DoesNotContain(listed!, e => e.ContactId == sib.Id && e.Kind == ContactRelationKind.Sibling);
    }

    [Fact]
    public async Task Readding_an_ended_relation_revives_it()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, ab, "Ann", "A");
        var b = await CreateContactAsync(api, ab, "Bo", "B");

        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Spouse);
        (await EndRelationAsync(api, a.Id, b.Id, ContactRelationKind.Spouse)).EnsureSuccessStatusCode();

        var revived = await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Spouse);   // remarried
        var edge = Assert.Single(revived.Relations);
        Assert.False(edge.Ended);
        Assert.Null(edge.Until);
    }

    [Fact]
    public async Task Ending_a_missing_edge_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, ab);
        var b = await CreateContactAsync(api, ab, "Bo", "B");

        Assert.Equal(HttpStatusCode.NotFound, (await EndRelationAsync(api, a.Id, b.Id, ContactRelationKind.Friend)).StatusCode);
    }

    [Fact]
    public async Task Parentage_cycles_are_refused_directly_and_transitively()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, ab, "Ann", "A");
        var b = await CreateContactAsync(api, ab, "Bo", "B");
        var c = await CreateContactAsync(api, ab, "Cy", "C");

        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Parent);   // b is a's parent
        await AddRelationAsync(api, b.Id, c.Id, ContactRelationKind.Parent);   // c is b's parent

        var direct = await api.PostAsJsonAsync($"/contacts/{b.Id}/relations", new AddContactRelationRequest { ToContactId = a.Id, Kind = ContactRelationKind.Parent });
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);

        var transitive = await api.PostAsJsonAsync($"/contacts/{c.Id}/relations", new AddContactRelationRequest { ToContactId = a.Id, Kind = ContactRelationKind.Parent });
        Assert.Equal(HttpStatusCode.BadRequest, transitive.StatusCode);

        // The Child spelling walks the same graph: "c is a's child" would also make a its own ancestor.
        var viaChild = await api.PostAsJsonAsync($"/contacts/{a.Id}/relations", new AddContactRelationRequest { ToContactId = c.Id, Kind = ContactRelationKind.Child });
        Assert.Equal(HttpStatusCode.BadRequest, viaChild.StatusCode);
    }

    [Fact]
    public async Task Emergency_contacts_keep_order_reject_self_and_bump_the_etag()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        var first = await CreateContactAsync(api, ab, "First", "Responder");
        var second = await CreateContactAsync(api, ab, "Second", "Responder");

        var self = await api.PutAsJsonAsync($"/contacts/{c.Id}/emergency-contacts", new SetEmergencyContactsRequest { ContactIds = [c.Id] });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        var resp = await api.PutAsJsonAsync($"/contacts/{c.Id}/emergency-contacts", new SetEmergencyContactsRequest { ContactIds = [second.Id, first.Id] });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal([second.Id, first.Id], dto.EmergencyContactIds);   // order = priority
        Assert.NotEqual(c.Etag, dto.Etag);
    }

    [Fact]
    public async Task Emergency_designation_round_trips_the_dav_seam_as_related_type_emergency()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        var er = await CreateContactAsync(api, ab, "First", "Responder");
        (await api.PutAsJsonAsync($"/contacts/{c.Id}/emergency-contacts", new SetEmergencyContactsRequest { ContactIds = [er.Id] })).EnsureSuccessStatusCode();

        var vcf = await api.GetStringAsync($"/dav-backend/u/{Uri.EscapeDataString(Email)}/collections/{ab}/resources/{c.ExternalId}");
        Assert.Contains($"RELATED;TYPE=emergency:urn:uuid:{er.Id:D}", vcf);

        // RELATED is preserve-if-absent on PUT — a client that drops the lines must not clear the designation.
        (await PutVcfAsync(api, Email, ab, c.ExternalId, MinimalVcf(c.ExternalId, "Jane Doe"))).EnsureSuccessStatusCode();
        var kept = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!;
        Assert.Equal([er.Id], kept.EmergencyContactIds);
    }

    [Fact]
    public async Task Put_without_related_lines_preserves_relations_in_both_directions()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var husband = await CreateContactAsync(api, ab, "Albin", "Spouse");
        var wife = await CreateContactAsync(api, ab, "Charlotta", "Spouse");
        await AddRelationAsync(api, husband.Id, wife.Id, ContactRelationKind.Partner);

        (await PutVcfAsync(api, Email, ab, husband.ExternalId, MinimalVcf(husband.ExternalId, "Albin Spouse"))).EnsureSuccessStatusCode();

        var after = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{husband.Id}"))!;
        var edge = Assert.Single(after.Relations);
        Assert.Equal(wife.Id, edge.ToContactId);
        Assert.Equal(ContactRelationKind.Partner, edge.Kind);

        var inverse = await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{wife.Id}/relations");
        Assert.Contains(inverse!, e => e.ContactId == husband.Id && e.Kind == ContactRelationKind.Partner);
    }

    [Fact]
    public async Task Put_with_related_lines_still_replaces_wholesale()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        var friend = await CreateContactAsync(api, ab, "Fay", "Friend");
        var other = await CreateContactAsync(api, ab, "Otto", "Other");
        await AddRelationAsync(api, c.Id, friend.Id, ContactRelationKind.Friend);

        var vcf = $"BEGIN:VCARD\r\nVERSION:4.0\r\nUID:{c.ExternalId}\r\nFN:Jane Doe\r\nRELATED;TYPE=friend:urn:uuid:{other.Id:D}\r\nEND:VCARD\r\n";
        (await PutVcfAsync(api, Email, ab, c.ExternalId, vcf)).EnsureSuccessStatusCode();

        var after = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!;
        Assert.Equal(other.Id, Assert.Single(after.Relations).ToContactId);
    }
}
