using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The member-facing existence check sibling services run with the caller's exchanged token: only
/// contacts in address books the caller can read come back.</summary>
public sealed class ContactLookupTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Alice = "alice@x.test";
    private const string Bob = "bob@x.test";

    [Fact]
    public async Task Returns_readable_contacts_and_omits_unknown_deleted_and_foreign()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs");
        var jane = await CreateContactAsync(alice, aliceBook, "Jane", "Doe");
        var gone = await CreateContactAsync(alice, aliceBook, "Gone", "Soon");
        (await alice.DeleteAsync($"/contacts/{gone.Id}")).EnsureSuccessStatusCode();
        var foreign = await CreateContactAsync(bob, bobBook, "Private", "Person");

        var resp = await alice.PostAsJsonAsync("/contacts/lookup",
            new LookupContactsRequest { ContactIds = [jane.Id, gone.Id, foreign.Id, Guid.NewGuid()] });
        resp.EnsureSuccessStatusCode();

        var only = Assert.Single((await resp.Content.ReadFromJsonAsync<List<ContactRef>>())!);
        Assert.Equal(jane.Id, only.ContactId);
        Assert.Equal("Jane Doe", only.DisplayName);
    }

    [Fact]
    public async Task Includes_contacts_in_books_shared_with_the_caller()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var bobBook = await CreateAddressBookAsync(bob, "bobs");
        var shared = await CreateContactAsync(bob, bobBook, "Shared", "Friend");
        _ = await alice.GetAsync("/contacts");

        var before = await (await alice.PostAsJsonAsync("/contacts/lookup", new LookupContactsRequest { ContactIds = [shared.Id] }))
            .Content.ReadFromJsonAsync<List<ContactRef>>();
        Assert.Empty(before!);

        (await bob.PostAsJsonAsync($"/address-books/{bobBook}/owners", new GrantOwnerRequest { Email = Alice, Access = "read" }))
            .EnsureSuccessStatusCode();

        var after = await (await alice.PostAsJsonAsync("/contacts/lookup", new LookupContactsRequest { ContactIds = [shared.Id] }))
            .Content.ReadFromJsonAsync<List<ContactRef>>();
        Assert.Equal(shared.Id, Assert.Single(after!).ContactId);
    }

    [Fact]
    public async Task Caps_the_batch_and_rejects_empty()
    {
        var alice = Factory.ApiClient(Alice);

        var tooMany = await alice.PostAsJsonAsync("/contacts/lookup",
            new LookupContactsRequest { ContactIds = [.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())] });
        var empty = await alice.PostAsJsonAsync("/contacts/lookup", new LookupContactsRequest { ContactIds = [] });

        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var resp = await Factory.AnonymousClient().PostAsJsonAsync("/contacts/lookup",
            new LookupContactsRequest { ContactIds = [Guid.NewGuid()] });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
