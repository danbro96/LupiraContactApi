using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Address-book collection management: list/create, idempotent personal bootstrap, and the
/// owner grant/revoke lifecycle including the last-owner guard.</summary>
public sealed class AddressBooksTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Alice = "alice@x.test";
    private const string Bob = "bob@x.test";

    [Fact]
    public async Task Bootstrap_is_idempotent_and_seeds_the_personal_book()
    {
        var api = Factory.ApiClient(Alice);

        var first = await api.PostAsync("/me/bootstrap", null);
        first.EnsureSuccessStatusCode();
        var books = await first.Content.ReadFromJsonAsync<List<AddressBookDto>>();
        Assert.Single(books!, b => b.Slug == "personal");

        var second = await api.PostAsync("/me/bootstrap", null);
        var again = await second.Content.ReadFromJsonAsync<List<AddressBookDto>>();
        Assert.Equal(books!.Count, again!.Count);
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
