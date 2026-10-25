using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Residencies;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Residencies over REST: add/revise/move-out/remove, the date and overlap rules, idempotent replay, write
/// authorization, and the contact's version tag staying untouched.</summary>
public sealed class ResidenciesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task<List<ResidencyDto>> ListAsync(HttpClient api, Guid contactId) =>
        (await api.GetFromJsonAsync<List<ResidencyDto>>($"/contacts/{contactId}/residencies"))!;

    [Fact]
    public async Task Add_lists_current_first_and_leaves_the_contact_etag_alone()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var (former, home, cabin) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await AddResidencyAsync(api, c.Id, former, movedIn: new FuzzyDate(2010), movedOut: new FuzzyDate(2015, 6));
        await AddResidencyAsync(api, c.Id, home, movedIn: new FuzzyDate(2015, 6));
        var vacation = await PostResidencyAsync(api, c.Id, cabin, ContactAddressType.Vacation, label: " Summer house ");
        Assert.Equal("Summer house", (await vacation.Content.ReadFromJsonAsync<ResidencyDto>())!.Label);

        var listed = await ListAsync(api, c.Id);
        Assert.Equal(former, listed[^1].PlaceId);
        Assert.Equal(c.Etag, (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!.Etag);
    }

    [Fact]
    public async Task Rejects_missing_places_bad_dates_and_overlaps()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var place = Guid.NewGuid();

        var noPlace = await api.PostAsync($"/contacts/{c.Id}/residencies", new StringContent("""{"type":"Home"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, noPlace.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostResidencyAsync(api, c.Id, Guid.Empty)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostResidencyAsync(api, c.Id, place, movedIn: new FuzzyDate(2015, 13))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostResidencyAsync(api, c.Id, place, movedIn: new FuzzyDate(2015, null, 12))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostResidencyAsync(api, c.Id, place, movedIn: new FuzzyDate(2016), movedOut: new FuzzyDate(2015))).StatusCode);

        await AddResidencyAsync(api, c.Id, place, movedIn: new FuzzyDate(2010), movedOut: new FuzzyDate(2019));
        Assert.Equal(HttpStatusCode.BadRequest, (await PostResidencyAsync(api, c.Id, place, movedIn: new FuzzyDate(2012))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostResidencyAsync(api, c.Id, place, movedIn: new FuzzyDate(2019))).StatusCode);   // moved back
    }

    [Fact]
    public async Task Revise_move_out_and_remove()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var r = await AddResidencyAsync(api, c.Id, Guid.NewGuid(), movedIn: new FuzzyDate(2010));

        var revised = await api.PutAsJsonAsync($"/residencies/{r.Id}", new ResidencyRequest { PlaceId = r.PlaceId, Type = ContactAddressType.Vacation, MovedIn = new FuzzyDate(2011) });
        Assert.Equal((ContactAddressType.Vacation, new FuzzyDate(2011)), ((await revised.Content.ReadFromJsonAsync<ResidencyDto>())!.Type, (await ListAsync(api, c.Id))[0].MovedIn));

        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/residencies/{r.Id}/move-out", new MoveOutRequest { MovedOut = new FuzzyDate(2009) })).StatusCode);
        var moved = await api.PostAsJsonAsync($"/residencies/{r.Id}/move-out", new MoveOutRequest { MovedOut = new FuzzyDate(2020, 5) });
        Assert.Equal(new FuzzyDate(2020, 5), (await moved.Content.ReadFromJsonAsync<ResidencyDto>())!.MovedOut);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/residencies/{r.Id}")).StatusCode);
        Assert.Empty(await ListAsync(api, c.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/residencies/{r.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_replayed_command_starts_one_residency()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var key = Guid.CreateVersion7();

        async Task<ResidencyDto> Send()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/contacts/{c.Id}/residencies")
            {
                Content = JsonContent.Create(new ResidencyRequest { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home }),
            };
            req.Headers.Add("Idempotency-Key", key.ToString());
            var resp = await api.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<ResidencyDto>())!;
        }

        var first = await Send();
        var again = await Send();
        Assert.Equal(first.Id, again.Id);
        Assert.Single(await ListAsync(api, c.Id));
    }

    [Fact]
    public async Task A_repeated_create_with_the_same_source_key_returns_the_same_residency()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var (c, other) = (await CreateContactAsync(api, book), await CreateContactAsync(api, book));
        var key = $"{Guid.CreateVersion7()}";
        var place = Guid.NewGuid();

        var first = await PostKeyedAsync(c.Id, new ResidencyRequest { PlaceId = place, Type = ContactAddressType.Home, MovedIn = new FuzzyDate(2020), SourceKey = key });
        var replay = await PostKeyedAsync(c.Id, new ResidencyRequest { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Work, SourceKey = $" {key} " });

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(place, replay.PlaceId);   // the second body is ignored
        Assert.Single(await ListAsync(api, c.Id));
        Assert.NotEqual(first.Id, (await PostKeyedAsync(other.Id, new ResidencyRequest { PlaceId = place, Type = ContactAddressType.Home, SourceKey = key })).Id);

        (await api.DeleteAsync($"/residencies/{first.Id}")).EnsureSuccessStatusCode();
        var fresh = await PostKeyedAsync(c.Id, new ResidencyRequest { PlaceId = place, Type = ContactAddressType.Vacation, SourceKey = key });
        Assert.Equal((first.Id, ContactAddressType.Vacation, (FuzzyDate?) null), (fresh.Id, fresh.Type, fresh.MovedIn));
        Assert.Single(await ListAsync(api, c.Id));

        async Task<ResidencyDto> PostKeyedAsync(Guid contactId, ResidencyRequest request)
        {
            var resp = await api.PostAsJsonAsync($"/contacts/{contactId}/residencies", request);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return (await resp.Content.ReadFromJsonAsync<ResidencyDto>())!;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_first_creates_with_the_same_source_key_return_one_residency(bool sameIdempotencyKey)
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var (key, place, commandId) = ($"{Guid.CreateVersion7()}", Guid.NewGuid(), Guid.CreateVersion7());

        var added = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/contacts/{c.Id}/residencies")
            {
                Content = JsonContent.Create(new ResidencyRequest { PlaceId = place, Type = ContactAddressType.Home, SourceKey = key }),
            };
            if (sameIdempotencyKey) req.Headers.Add("Idempotency-Key", commandId.ToString());
            var resp = await api.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return (await resp.Content.ReadFromJsonAsync<ResidencyDto>())!.Id;
        }));

        var id = Assert.Single(added.Distinct());
        Assert.Equal(id, Assert.Single(await ListAsync(api, c.Id)).Id);
    }

    [Fact]
    public async Task Writing_needs_write_on_the_contacts_book()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var book = await CreateAddressBookAsync(alice);
        var c = await CreateContactAsync(alice, book);
        await GrantAsync(alice, book, "bob@x.test", "read");

        Assert.Empty(await ListAsync(bob, c.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostResidencyAsync(bob, c.Id, Guid.NewGuid())).StatusCode);
    }
}
