using System.Net;
using System.Net.Http.Json;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

public sealed class ContactsRestTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Create_then_get_and_list()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId, "Jane", "Doe", "jane@x.test");
        Assert.Equal("Jane Doe", contact.DisplayName);

        var got = await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}");
        Assert.Equal(contact.Id, got!.Id);

        var list = await api.GetFromJsonAsync<List<ContactDto>>($"/contacts?addressBookId={abId}");
        Assert.Contains(list!, c => c.Id == contact.Id);
    }

    [Fact]
    public async Task Query_matches_by_name()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        await CreateContactAsync(api, abId, "Zaphod", "Beeblebrox");
        await CreateContactAsync(api, abId, "Arthur", "Dent");

        var hits = await api.GetFromJsonAsync<List<ContactDto>>("/contacts?query=Zaphod");
        Assert.Contains(hits!, c => c.DisplayName.Contains("Zaphod"));
        Assert.DoesNotContain(hits!, c => c.DisplayName.Contains("Arthur"));
    }

    [Fact]
    public async Task Revise_merges_new_fields_without_wiping_existing()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId, "Jane", "Doe", "jane@x.test");

        // Enrichment: add a phone + a second email, set a birthday. Name + original email must survive.
        var resp = await api.PutAsJsonAsync($"/contacts/{contact.Id}", new ReviseContactRequest
        {
            Channels =
            [
                new ContactReachChannel(ReachMedium.Email, "jane.doe@work.test", "work", false),
                new ContactReachChannel(ReachMedium.Phone, "+46700000000", "cell", false),
            ],
            Birthday = new DateOnly(1990, 4, 1),
        });
        resp.EnsureSuccessStatusCode();
        var revised = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;

        Assert.Equal("Jane Doe", revised.DisplayName);                                                   // name kept
        Assert.Contains(revised.Channels, ch => ch.Value == "jane@x.test");                              // original email kept
        Assert.Contains(revised.Channels, ch => ch.Value == "jane.doe@work.test");                       // new email added
        Assert.Contains(revised.Channels, ch => ch.Medium == ReachMedium.Phone && ch.Value == "+46700000000");
        Assert.Equal(new DateOnly(1990, 4, 1), revised.Birthday);
    }

    [Fact]
    public async Task Revise_without_write_access_is_forbidden()
    {
        var owner = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(owner);
        var contact = await CreateContactAsync(owner, abId, "Jane", "Doe");

        var stranger = Factory.ApiClient("mallory@x.test");
        var resp = await stranger.PutAsJsonAsync($"/contacts/{contact.Id}", new ReviseContactRequest { Nickname = "hax" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_then_get_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/contacts/{contact.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/contacts/{contact.Id}")).StatusCode);
    }
}
