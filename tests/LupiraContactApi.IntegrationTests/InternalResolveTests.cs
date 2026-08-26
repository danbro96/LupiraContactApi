using LupiraContactApi.Core.Domain;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Internal;
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
        var svc = Factory.ServiceClient();
        var book = await CreateAddressBookAsync(api);
        var jane = await CreateContactAsync(api, book, "Jane", "Doe");
        var gone = await CreateContactAsync(api, book, "Gone", "Soon");
        (await api.DeleteAsync($"/contacts/{gone.Id}")).EnsureSuccessStatusCode();

        var resp = await svc.PostAsJsonAsync("/internal/contacts/resolve",
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
        var resp = await Factory.ServiceClient().PostAsJsonAsync("/internal/contacts/resolve",
            new ResolveContactsRequest { ContactIds = [.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())] });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Birthdays_lists_live_contacts_with_a_birthday_and_omits_the_rest()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);

        var dated = (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = book, GivenName = "Ada", FamilyName = "Byron", Birthday = new PartialDate(1815, 12, 10),
        })).Content.ReadFromJsonAsync<ContactDto>())!;
        var yearless = (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = book, GivenName = "Grace", FamilyName = "Hopper", Birthday = new PartialDate(null, 12, 9),
        })).Content.ReadFromJsonAsync<ContactDto>())!;
        var deceased = (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = book, GivenName = "Alan", FamilyName = "Turing", Birthday = new PartialDate(1912, 6, 23),
        })).Content.ReadFromJsonAsync<ContactDto>())!;
        (await api.PutAsJsonAsync($"/contacts/{deceased.Id}/deceased", new SetDeceasedRequest { DeathDate = null })).EnsureSuccessStatusCode();
        _ = await CreateContactAsync(api, book, "No", "Birthday");   // no birthday → omitted

        var result = await (await Factory.ServiceClient().GetAsync("/internal/contacts/birthdays")).EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<ContactBirthdaysResponse>();

        var byId = result!.Contacts.ToDictionary(c => c.ContactId);
        Assert.Equal(new[] { dated.Id, yearless.Id }.OrderBy(x => x), byId.Keys.OrderBy(x => x));
        Assert.Equal((1815, 12, 10), (byId[dated.Id].Year, byId[dated.Id].Month, byId[dated.Id].Day));
        Assert.Equal((null, 12, 9), (byId[yearless.Id].Year, byId[yearless.Id].Month, byId[yearless.Id].Day));
        Assert.DoesNotContain(deceased.Id, byId.Keys);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var anon = Factory.AnonymousClient();
        var resp = await anon.PostAsJsonAsync("/internal/contacts/resolve",
            new ResolveContactsRequest { ContactIds = [Guid.NewGuid()] });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Rejects_tokens_without_the_internal_scope()
    {
        var user = Factory.ApiClient(Email);
        var resp = await user.PostAsJsonAsync("/internal/contacts/resolve",
            new ResolveContactsRequest { ContactIds = [Guid.NewGuid()] });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, resp.StatusCode);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await user.GetAsync("/internal/contacts/birthdays")).StatusCode);
    }
}
