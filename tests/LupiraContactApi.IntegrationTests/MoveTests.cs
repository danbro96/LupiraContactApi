using System.Net;
using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Residencies;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The batch move: a family told once, each one's residency at the old place ended and one at the new place
/// started, all or nothing.</summary>
public sealed class MoveTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task A_family_moves_together()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var (a, b) = (await CreateContactAsync(api, book, "Ann", "A"), await CreateContactAsync(api, book, "Bo", "B"));
        var (oldHome, newHome, cabin) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var c in new[] { a, b }) await AddResidencyAsync(api, c.Id, oldHome, movedIn: new FuzzyDate(2015));
        await AddResidencyAsync(api, a.Id, cabin, ContactAddressType.Vacation);

        var resp = await api.PostAsJsonAsync("/moves", new MoveRequest
        {
            ContactIds = [a.Id, b.Id], FromPlaceId = oldHome, ToPlaceId = newHome, Type = ContactAddressType.Home, MovedIn = new FuzzyDate(2026, 5),
        });
        resp.EnsureSuccessStatusCode();
        Assert.Equal(2, (await resp.Content.ReadFromJsonAsync<List<ResidencyDto>>())!.Count);

        foreach (var c in new[] { a, b })
        {
            var listed = (await api.GetFromJsonAsync<List<ResidencyDto>>($"/contacts/{c.Id}/residencies"))!;
            Assert.Equal(new FuzzyDate(2026, 5), listed.Single(r => r.PlaceId == oldHome).MovedOut);
            Assert.Contains(listed, r => r.PlaceId == newHome && r.MovedIn == new FuzzyDate(2026, 5) && r.MovedOut is null);
        }

        Assert.Contains((await api.GetFromJsonAsync<List<ResidencyDto>>($"/contacts/{a.Id}/residencies"))!, r => r.PlaceId == cabin && r.MovedOut is null);
    }

    [Fact]
    public async Task One_contact_the_caller_cannot_write_refuses_the_whole_move()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var mine = await CreateContactAsync(bob, bobBook, "Mine", "Bob");
        var hers = await CreateContactAsync(alice, aliceBook, "Hers", "Alice");
        await GrantAsync(alice, aliceBook, "bob@x.test", "read");

        var resp = await bob.PostAsJsonAsync("/moves", new MoveRequest { ContactIds = [mine.Id, hers.Id], ToPlaceId = Guid.NewGuid(), Type = ContactAddressType.Home, MovedIn = new FuzzyDate(2026) });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<List<ResidencyDto>>($"/contacts/{mine.Id}/residencies"))!);
    }
}
