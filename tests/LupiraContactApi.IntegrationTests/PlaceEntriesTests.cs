using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Door codes per place: visible to readers of a current resident, writable by its writers, gone from view once
/// everyone moved out, fed to mirrors, and never on a phone-synced card.</summary>
public sealed class PlaceEntriesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static Task<HttpResponseMessage> SetAsync(HttpClient api, Guid placeId, Guid codeId, string code) =>
        api.PutAsJsonAsync($"/places/{placeId}/entry-codes/{codeId}", new SetEntryCodeRequest { Label = "Port", Code = code });

    [Fact]
    public async Task Residents_readers_see_the_codes_and_writers_set_them()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var book = await CreateAddressBookAsync(alice);
        var mum = await CreateContactAsync(alice, book, "Mum", "M");
        var place = Guid.NewGuid();
        await AddResidencyAsync(alice, mum.Id, place);

        (await SetAsync(alice, place, Guid.NewGuid(), "1234")).EnsureSuccessStatusCode();
        Assert.Equal("1234", Assert.Single((await alice.GetFromJsonAsync<PlaceEntryDto>($"/places/{place}/entry"))!.Codes).Code);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/places/{place}/entry")).StatusCode);
        await GrantAsync(alice, book, "bob@x.test", "read");
        Assert.Single((await bob.GetFromJsonAsync<PlaceEntryDto>($"/places/{place}/entry"))!.Codes);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(bob, place, Guid.NewGuid(), "0000")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(alice, Guid.NewGuid(), Guid.NewGuid(), "0000")).StatusCode);   // nobody lives there
    }

    [Fact]
    public async Task Codes_leave_view_when_everyone_moved_out()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var place = Guid.NewGuid();
        var home = await AddResidencyAsync(api, c.Id, place);
        (await SetAsync(api, place, Guid.NewGuid(), "1234")).EnsureSuccessStatusCode();
        var full = (await api.GetFromJsonAsync<PlaceEntryChangesResponse>("/sync/place-entries", Json))!;
        Assert.Equal(place, Assert.Single(full.Changed).PlaceId);

        (await api.PostAsJsonAsync($"/residencies/{home.Id}/move-out", new MoveOutRequest { MovedOut = new FuzzyDate(2020) })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/places/{place}/entry")).StatusCode);
        var delta = (await api.GetFromJsonAsync<PlaceEntryChangesResponse>($"/sync/place-entries?since={Uri.EscapeDataString(full.Cursor)}", Json))!;
        Assert.Equal(place, Assert.Single(delta.Deleted));
    }

    [Fact]
    public async Task Codes_never_reach_a_phone_synced_card()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, book);
        var place = Guid.NewGuid();
        await AddResidencyAsync(api, c.Id, place);
        (await SetAsync(api, place, Guid.NewGuid(), "SECRET-4711")).EnsureSuccessStatusCode();

        var card = await api.GetStringAsync($"/dav-backend/u/{Uri.EscapeDataString(Email)}/collections/{book}/resources/{c.ExternalId}");
        Assert.DoesNotContain("SECRET-4711", card);
    }
}
