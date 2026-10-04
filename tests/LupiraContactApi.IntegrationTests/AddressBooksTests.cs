using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Me;
using LupiraContactApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Address-book collection management: list/create, idempotent personal bootstrap (the caller's own book, never a
/// shared one; pre-existing sole-owned ones adopted), and the owner grant/revoke lifecycle including the last-owner guard.</summary>
public sealed class AddressBooksTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Alice = "alice@x.test";
    private const string Bob = "bob@x.test";

    private static async Task<List<AddressBookDto>> BootstrapAsync(HttpClient api)
    {
        var resp = await api.PostAsync("/me/bootstrap", null);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<AddressBookDto>>())!;
    }

    private static async Task<Guid> SelfContactBookAsync(HttpClient api)
    {
        var self = (await api.GetFromJsonAsync<MeDto>("/me"))!.ContactId!.Value;
        return (await api.GetFromJsonAsync<ContactDto>($"/contacts/{self}"))!.AddressBookId;
    }

    [Fact]
    public async Task Bootstrap_is_idempotent_and_seeds_the_personal_book()
    {
        var api = Factory.ApiClient(Alice);

        var personal = Assert.Single(await BootstrapAsync(api));
        Assert.Equal(("personal", true), (personal.Slug, personal.IsPersonal));

        Assert.Equal(personal.Id, Assert.Single(await BootstrapAsync(api)).Id);
        Assert.Equal(personal.Id, await SelfContactBookAsync(api));
    }

    [Fact]
    public async Task Another_members_shared_personal_book_does_not_stand_in_for_the_callers_own()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var alicePersonal = Assert.Single(await BootstrapAsync(alice)).Id;
        await GrantAsync(alice, alicePersonal, Bob, "owner");

        var books = await BootstrapAsync(bob);

        Assert.Equal(2, books.Count(b => b.Slug == "personal"));
        var own = Assert.Single(books, b => b.IsPersonal);
        Assert.NotEqual(alicePersonal, own.Id);
        Assert.Equal(own.Id, await SelfContactBookAsync(bob));
        Assert.Equal(own.Id, Assert.Single((await bob.GetFromJsonAsync<SyncContainersResponse>("/sync/containers"))!.AddressBooks, b => b.IsPersonal).Id);
        Assert.Equal(alicePersonal, Assert.Single((await alice.GetFromJsonAsync<List<AddressBookDto>>("/address-books"))!, b => b.IsPersonal).Id);

        Assert.Equal(books.Select(b => (b.Id, b.IsPersonal)).Order(), (await BootstrapAsync(bob)).Select(b => (b.Id, b.IsPersonal)).Order());
    }

    [Fact]
    public async Task A_pre_existing_personal_book_is_adopted_by_its_sole_owner_not_by_a_reader()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        // As bootstrap created them before a book recorded whose it is.
        var legacy = await CreateAddressBookAsync(alice, "personal", "Personal");
        await GrantAsync(alice, legacy, Bob, "read");

        var bobBooks = await BootstrapAsync(bob);
        Assert.False(Assert.Single(bobBooks, b => b.Id == legacy).IsPersonal);
        Assert.NotEqual(legacy, Assert.Single(bobBooks, b => b.IsPersonal).Id);

        var adopted = Assert.Single(await BootstrapAsync(alice));
        Assert.Equal((legacy, true), (adopted.Id, adopted.IsPersonal));
        Assert.Equal(legacy, await SelfContactBookAsync(alice));
        Assert.Equal(legacy, Assert.Single(await BootstrapAsync(alice)).Id);
    }

    [Fact]
    public async Task A_pre_existing_personal_book_with_several_owners_is_left_unclaimed()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var legacy = await CreateAddressBookAsync(alice, "personal", "Personal");
        await GrantAsync(alice, legacy, Bob, "owner");

        foreach (var api in new[] { alice, bob })
        {
            var books = await BootstrapAsync(api);
            Assert.False(Assert.Single(books, b => b.Id == legacy).IsPersonal);
            Assert.NotEqual(legacy, Assert.Single(books, b => b.IsPersonal).Id);
        }
    }

    [Fact]
    public async Task Concurrent_bootstraps_converge_on_one_personal_book()
    {
        await GetMyIdAsync(Factory.ApiClient(Alice));   // provision first; the race under test is the book's

        var lists = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => BootstrapAsync(Factory.ApiClient(Alice))));

        Assert.Single(lists.Select(l => Assert.Single(l).Id).Distinct());
    }

    [Fact]
    public async Task Create_grants_the_caller_owner_and_lists_it()
    {
        var api = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(api, "family", "Family");

        var books = await api.GetFromJsonAsync<List<AddressBookDto>>("/address-books");
        var book = Assert.Single(books!, b => b.Id == id);
        Assert.Equal(Access.Owner, book.Access);
    }

    [Fact]
    public async Task Books_are_not_visible_to_non_members()
    {
        var alice = Factory.ApiClient(Alice);
        await CreateAddressBookAsync(alice, "family");

        var bob = Factory.ApiClient(Bob);
        var books = await bob.GetFromJsonAsync<List<AddressBookDto>>("/address-books");
        Assert.Empty(books!);
    }

    [Fact]
    public async Task Grant_and_revoke_share_the_book_and_upsert_on_regrant()
    {
        var alice = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(alice, "family");

        var grant = await alice.PostAsJsonAsync($"/address-books/{id}/owners", new GrantOwnerRequest { Email = Bob, Access = "read" });
        grant.EnsureSuccessStatusCode();
        var dto = await grant.Content.ReadFromJsonAsync<OwnerGrantDto>();
        Assert.Equal(Access.Read, dto!.Access);

        var bob = Factory.ApiClient(Bob);
        Assert.Single((await bob.GetFromJsonAsync<List<AddressBookDto>>("/address-books"))!);

        // Re-grant upserts the level instead of duplicating.
        var regrant = await alice.PostAsJsonAsync($"/address-books/{id}/owners", new GrantOwnerRequest { Email = Bob, Access = "read-write" });
        Assert.Equal(Access.ReadWrite, (await regrant.Content.ReadFromJsonAsync<OwnerGrantDto>())!.Access);

        var revoke = await alice.DeleteAsync($"/address-books/{id}/owners?email={Bob}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<List<AddressBookDto>>("/address-books"))!);
    }

    [Fact]
    public async Task Revoking_the_last_owner_conflicts()
    {
        var alice = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(alice, "family");

        var revoke = await alice.DeleteAsync($"/address-books/{id}/owners?email={Alice}");
        Assert.Equal(HttpStatusCode.Conflict, revoke.StatusCode);
    }

    [Fact]
    public async Task Grants_are_owner_only()
    {
        var alice = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(alice, "family");
        await alice.PostAsJsonAsync($"/address-books/{id}/owners", new GrantOwnerRequest { Email = Bob, Access = "read-write" });

        // Bob is read-write but not owner — granting is forbidden.
        var bob = Factory.ApiClient(Bob);
        var attempt = await bob.PostAsJsonAsync($"/address-books/{id}/owners", new GrantOwnerRequest { Email = "carol@x.test" });
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }
}
