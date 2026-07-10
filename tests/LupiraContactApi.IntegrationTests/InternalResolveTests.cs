using LupiraContactApi.Handlers;
using System.Net.Http.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The service-to-service resolve seam (cal-api's IContactResolver): existence + display name only,
/// unknown and deleted ids silently absent.</summary>
public sealed class InternalResolveTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    [Fact]
    public async Task Resolves_live_contacts_and_omits_unknown_and_deleted()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var jane = await CreateContactAsync(api, book, "Jane", "Doe");
        var gone = await CreateContactAsync(api, book, "Gone", "Soon");
        (await api.DeleteAsync($"/contacts/{gone.Id}")).EnsureSuccessStatusCode();

        var resp = await api.PostAsJsonAsync("/internal/contacts/resolve",
            new ResolveContactsRequest { ContactIds = [jane.Id, gone.Id, Guid.NewGuid()] });
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<ResolveContactsResponse>();
        var only = Assert.Single(result!.Contacts);
        Assert.Equal(jane.Id, only.ContactId);
        Assert.Equal("Jane Doe", only.DisplayName);
    }

    [Fact]
    public async Task Caps_the_id_batch()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync("/internal/contacts/resolve",
            new ResolveContactsRequest { ContactIds = [.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())] });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var anon = Factory.AnonymousClient();
        var resp = await anon.PostAsJsonAsync("/internal/contacts/resolve",
            new ResolveContactsRequest { ContactIds = [Guid.NewGuid()] });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
