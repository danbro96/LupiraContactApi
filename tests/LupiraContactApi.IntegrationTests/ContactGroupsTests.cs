using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

public sealed class ContactGroupsTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Create_list_member_rename_delete_lifecycle()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId);

        var created = await api.PostAsync($"/address-books/{abId}/groups?kind=organization&name=Acme", null);
        created.EnsureSuccessStatusCode();
        var group = (await created.Content.ReadFromJsonAsync<ContactGroupDto>())!;
        Assert.Equal(ContactGroupKind.Organization, group.Kind);
        Assert.Equal("Acme", group.Name);

        var list = await api.GetFromJsonAsync<List<ContactGroupDto>>($"/address-books/{abId}/groups");
        Assert.Contains(list!, g => g.Id == group.Id);

        var added = await api.PostAsync($"/groups/{group.Id}/members?contactId={contact.Id}&role=Engineer", null);
        added.EnsureSuccessStatusCode();
        Assert.Contains((await added.Content.ReadFromJsonAsync<ContactGroupDto>())!.Members, m => m.ContactId == contact.Id && m.Role == "Engineer");

        var removed = await api.DeleteAsync($"/groups/{group.Id}/members/{contact.Id}");
        removed.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await removed.Content.ReadFromJsonAsync<ContactGroupDto>())!.Members, m => m.ContactId == contact.Id);

        var renamed = await api.PutAsync($"/groups/{group.Id}?name=AcmeCorp", null);
        renamed.EnsureSuccessStatusCode();
        Assert.Equal("AcmeCorp", (await renamed.Content.ReadFromJsonAsync<ContactGroupDto>())!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/groups/{group.Id}")).StatusCode);
        var after = await api.GetFromJsonAsync<List<ContactGroupDto>>($"/address-books/{abId}/groups");
        Assert.DoesNotContain(after!, g => g.Id == group.Id);
    }
}
