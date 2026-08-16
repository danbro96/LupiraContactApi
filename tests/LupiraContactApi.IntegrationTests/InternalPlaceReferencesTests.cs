using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using LupiraContactApi.Handlers;
using System.Net.Http.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The place-reference check seam for geo's orphan sweep: counts per requested place id, residency
/// history and deceased contacts included, deleted contacts and unrequested ids excluded.</summary>
public sealed class InternalPlaceReferencesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private static Task<HttpResponseMessage> SetAddressesAsync(HttpClient api, Guid contactId, params ContactPostalAddress[] addresses) =>
        api.PutAsJsonAsync($"/contacts/{contactId}/addresses", new SetContactAddressesRequest { Addresses = [.. addresses] });

    private static async Task<ContactPlaceReferencesResponse> CheckAsync(HttpClient svc, params Guid[] placeIds)
    {
        var resp = await svc.PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [.. placeIds] });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactPlaceReferencesResponse>())!;
    }

    [Fact]
    public async Task Counts_current_and_moved_out_addresses_for_requested_ids_only()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var home = Guid.NewGuid();
        var former = Guid.NewGuid();
        var unrequested = Guid.NewGuid();

        var jane = await CreateContactAsync(api, book, "Jane", "Doe");
        (await SetAddressesAsync(api, jane.Id,
            new ContactPostalAddress { PlaceId = home, Type = ContactAddressType.Home },
            new ContactPostalAddress { PlaceId = former, Type = ContactAddressType.Home, MovedOut = new FuzzyDate(2020, null, null) }))
            .EnsureSuccessStatusCode();

        var john = await CreateContactAsync(api, book, "John", "Doe");
        (await SetAddressesAsync(api, john.Id,
            new ContactPostalAddress { PlaceId = home, Type = ContactAddressType.Home },
            new ContactPostalAddress { PlaceId = unrequested, Type = ContactAddressType.Work }))
            .EnsureSuccessStatusCode();

        var result = await CheckAsync(Factory.ServiceClient(), home, former, Guid.NewGuid());

        var byId = result.Places.ToDictionary(p => p.PlaceId, p => p.Count);
        Assert.Equal(2, byId[home]);
        Assert.Equal(1, byId[former]);
        Assert.Equal(2, byId.Count);   // zero-ref requested id omitted, unrequested id absent
    }

    [Fact]
    public async Task Includes_deceased_and_excludes_deleted_contacts()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var keptPlace = Guid.NewGuid();
        var lostPlace = Guid.NewGuid();

        var deceased = await CreateContactAsync(api, book, "Alan", "Turing");
        (await SetAddressesAsync(api, deceased.Id, new ContactPostalAddress { PlaceId = keptPlace, Type = ContactAddressType.Home }))
            .EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync($"/contacts/{deceased.Id}/deceased", new SetDeceasedRequest { DeathDate = null }))
            .EnsureSuccessStatusCode();

        var deleted = await CreateContactAsync(api, book, "Gone", "Soon");
        (await SetAddressesAsync(api, deleted.Id, new ContactPostalAddress { PlaceId = lostPlace, Type = ContactAddressType.Home }))
            .EnsureSuccessStatusCode();
        (await api.DeleteAsync($"/contacts/{deleted.Id}")).EnsureSuccessStatusCode();

        var result = await CheckAsync(Factory.ServiceClient(), keptPlace, lostPlace);

        var only = Assert.Single(result.Places);
        Assert.Equal(keptPlace, only.PlaceId);
        Assert.Equal(1, only.Count);
    }

    [Fact]
    public async Task Caps_the_id_batch_and_rejects_empty()
    {
        var svc = Factory.ServiceClient();
        var empty = await svc.PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [] });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, empty.StatusCode);

        var oversize = await svc.PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [.. Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid())] });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, oversize.StatusCode);
    }

    [Fact]
    public async Task Requires_the_internal_scope()
    {
        var anon = await Factory.AnonymousClient().PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [Guid.NewGuid()] });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anon.StatusCode);

        var user = await Factory.ApiClient(Email).PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [Guid.NewGuid()] });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, user.StatusCode);
    }
}
