using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Sync;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Core.Dtos.Residencies;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Door codes per place: visible to readers of a current resident, writable by its writers, gone from view once
/// everyone moved out, fed to mirrors, and never on a phone-synced card.</summary>
public sealed class PlaceEntriesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static async Task<SyncPage<PlaceEntryDto>> ChangesAsync(HttpClient api, string? since = null, int? limit = null)
    {
        var qs = new List<string>();
        if (since is not null) qs.Add($"since={Uri.EscapeDataString(since)}");
        if (limit is not null) qs.Add($"limit={limit}");
        return (await api.GetFromJsonAsync<SyncPage<PlaceEntryDto>>("/sync/place-entries" + (qs.Count > 0 ? "?" + string.Join("&", qs) : ""), Json))!;
    }

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
        var full = await ChangesAsync(api);
        Assert.Equal(place, Assert.Single(full.Changed).PlaceId);

        (await api.PostAsJsonAsync($"/residencies/{home.Id}/move-out", new MoveOutRequest { MovedOut = new FuzzyDate(2020) })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/places/{place}/entry")).StatusCode);
        var delta = await ChangesAsync(api, full.Cursor);
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

    [Fact]
    public async Task Full_sync_pages_across_more_places_than_the_limit()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var places = new HashSet<Guid>();
        for (var n = 0; n < 5; n++)
        {
            var place = Guid.NewGuid();
            await AddResidencyAsync(api, c.Id, place, ContactAddressType.Work);
            (await SetAsync(api, place, Guid.NewGuid(), $"{n}")).EnsureSuccessStatusCode();
            places.Add(place);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        SyncPage<PlaceEntryDto> page;
        var pages = 0;
        do
        {
            page = await ChangesAsync(api, cursor, 2);
            seen.AddRange(page.Changed.Select(e => e.PlaceId));
            cursor = page.Cursor;
            Assert.True(++pages < 20, "paging loop did not terminate");
        } while (page.HasMore);

        Assert.Equal(3, pages);
        Assert.Equal(places.Count, seen.Count);
        Assert.Equal(places, seen.ToHashSet());
    }

    [Fact]
    public async Task Deleting_the_resident_tombstones_the_place_and_a_new_code_arrives_as_a_delta()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var stays = await CreateContactAsync(api, book, "Stays", "S");
        var leaves = await CreateContactAsync(api, book, "Leaves", "L");
        var (kept, gone) = (Guid.NewGuid(), Guid.NewGuid());
        await AddResidencyAsync(api, stays.Id, kept);
        await AddResidencyAsync(api, leaves.Id, gone);
        (await SetAsync(api, gone, Guid.NewGuid(), "1111")).EnsureSuccessStatusCode();
        var full = await ChangesAsync(api);
        Assert.Equal(gone, Assert.Single(full.Changed).PlaceId);

        (await api.DeleteAsync($"/contacts/{leaves.Id}")).EnsureSuccessStatusCode();
        (await SetAsync(api, kept, Guid.NewGuid(), "2222")).EnsureSuccessStatusCode();

        var delta = await ChangesAsync(api, full.Cursor);
        Assert.False(delta.Reset);
        Assert.Equal(kept, Assert.Single(delta.Changed).PlaceId);
        Assert.Equal(gone, Assert.Single(delta.Deleted));
    }

    [Fact]
    public async Task Another_users_places_never_reach_my_tombstones()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var mine = await CreateContactAsync(alice, await CreateAddressBookAsync(alice));
        var myPlace = Guid.NewGuid();
        await AddResidencyAsync(alice, mine.Id, myPlace);
        (await SetAsync(alice, myPlace, Guid.NewGuid(), "1111")).EnsureSuccessStatusCode();
        var full = await ChangesAsync(alice);

        var theirs = await CreateContactAsync(bob, await CreateAddressBookAsync(bob, "bobs", "Bobs"));
        var theirPlace = Guid.NewGuid();
        await AddResidencyAsync(bob, theirs.Id, theirPlace);
        (await SetAsync(bob, theirPlace, Guid.NewGuid(), "2222")).EnsureSuccessStatusCode();
        (await bob.DeleteAsync($"/contacts/{theirs.Id}")).EnsureSuccessStatusCode();
        (await alice.DeleteAsync($"/contacts/{mine.Id}")).EnsureSuccessStatusCode();

        var delta = await ChangesAsync(alice, full.Cursor);
        Assert.Empty(delta.Changed);
        Assert.Equal(myPlace, Assert.Single(delta.Deleted));
    }
}
