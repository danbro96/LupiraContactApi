using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Me;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>A principal is linked to its own contact: bootstrap links a readable contact carrying the login email, else creates
/// one in the personal book; <c>/me</c> links a match that appears later but never creates; an existing link is kept; a linked
/// contact can't be deleted.</summary>
public sealed class SelfContactTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Alice = "alice@x.test";
    const string Bob = "bob@x.test";
    const string Carol = "carol@x.test";

    static async Task<Guid?> MyContactIdAsync(HttpClient api) => (await api.GetFromJsonAsync<MeDto>("/me"))!.ContactId;

    static async Task<Guid> BootstrapAsync(HttpClient api)
    {
        var resp = await api.PostAsync("/me/bootstrap", null);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<AddressBookDto>>())!.Single(b => b.IsPersonal).Id;
    }

    static async Task<List<ContactDto>> ContactsInAsync(HttpClient api, Guid addressBookId) =>
        (await api.GetFromJsonAsync<List<ContactDto>>($"/contacts?addressBookId={addressBookId}"))!;

    [Fact]
    public async Task Bootstrap_links_an_email_match_in_a_shared_book()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var family = await CreateAddressBookAsync(alice, "family");
        var card = await CreateContactAsync(alice, family, "Bob", "Builder", email: "Bob@X.test");
        await GrantAsync(alice, family, Bob, "read");

        var personal = await BootstrapAsync(bob);

        Assert.Equal(card.Id, await MyContactIdAsync(bob));
        Assert.Empty(await ContactsInAsync(bob, personal));
    }

    [Fact]
    public async Task Bootstrap_without_a_match_creates_a_contact_in_personal_and_links_it()
    {
        var bob = Factory.ApiClient(Bob);

        var personal = await BootstrapAsync(bob);

        var created = Assert.Single(await ContactsInAsync(bob, personal));
        Assert.Equal(created.Id, await MyContactIdAsync(bob));
        Assert.Equal("bob", created.GivenName);   // the dev login's name is its email, so the local part stands in
        Assert.Equal(new ContactReachChannel(ReachMedium.Email, Bob, null, true), Assert.Single(created.Channels));

        await BootstrapAsync(bob);
        Assert.Equal(created.Id, Assert.Single(await ContactsInAsync(bob, personal)).Id);
    }

    [Fact]
    public async Task Several_matches_prefer_an_owned_book_then_the_most_recently_updated()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var carol = Factory.ApiClient(Carol);
        var shared = await CreateAddressBookAsync(alice, "shared");
        await GrantAsync(alice, shared, Bob, "read");
        await GrantAsync(alice, shared, Carol, "read");

        // Bob only reads: the most recently updated wins, not the first created.
        var older = await CreateContactAsync(alice, shared, "Bob", "Older", email: Bob);
        await CreateContactAsync(alice, shared, "Bob", "Newer", email: Bob);
        (await alice.PutAsJsonAsync($"/contacts/{older.Id}", new ReviseContactRequest { Notes = "touched" })).EnsureSuccessStatusCode();
        Assert.Equal(older.Id, await MyContactIdAsync(bob));

        // Carol owns a book with a match: it beats a more recent one she only reads.
        var own = await CreateContactAsync(carol, await CreateAddressBookAsync(carol, "mine"), "Carol", "Own", email: Carol);
        await CreateContactAsync(alice, shared, "Carol", "Shared", email: Carol);
        Assert.Equal(own.Id, await MyContactIdAsync(carol));
    }

    [Fact]
    public async Task Me_links_a_match_once_a_book_is_shared_later_but_never_creates()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);

        Assert.Null(await MyContactIdAsync(bob));
        Assert.Empty((await bob.GetFromJsonAsync<List<AddressBookDto>>("/address-books"))!);

        var family = await CreateAddressBookAsync(alice, "family");
        var card = await CreateContactAsync(alice, family, "Bob", "Builder", email: Bob);
        Assert.Null(await MyContactIdAsync(bob));
        await GrantAsync(alice, family, Bob, "read");

        Assert.Equal(card.Id, await MyContactIdAsync(bob));
    }

    [Fact]
    public async Task An_existing_link_is_kept()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var mine = await CreateContactAsync(bob, await CreateAddressBookAsync(bob, "mine"), "Me");
        (await bob.PutAsJsonAsync("/me/contact", new SetMyContactRequest { ContactId = mine.Id })).EnsureSuccessStatusCode();
        var family = await CreateAddressBookAsync(alice, "family");
        await CreateContactAsync(alice, family, "Bob", "Builder", email: Bob);
        await GrantAsync(alice, family, Bob, "read");

        var personal = await BootstrapAsync(bob);

        Assert.Equal(mine.Id, await MyContactIdAsync(bob));
        Assert.Empty(await ContactsInAsync(bob, personal));
    }

    [Fact]
    public async Task A_deleted_self_contact_comes_back_on_bootstrap()
    {
        var bob = Factory.ApiClient(Bob);
        await BootstrapAsync(bob);
        var self = (await MyContactIdAsync(bob))!.Value;
        // Deleted before linked contacts became undeletable — no surface can do this now.
        await using (var session = Store.LightweightSession())
        {
            session.Events.Append(self, new ContactDeleted(self));
            await session.SaveChangesAsync();
        }

        await BootstrapAsync(bob);

        Assert.Equal(self, await MyContactIdAsync(bob));
        (await bob.GetAsync($"/contacts/{self}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Concurrent_ensures_converge_on_one_contact()
    {
        var bob = Factory.ApiClient(Bob);
        var principalId = await GetMyIdAsync(bob);
        Guid personal;
        await using (var scope = Factory.Services.CreateAsyncScope())
            personal = await scope.ServiceProvider.GetRequiredService<AddressBookService>().EnsurePersonalBookAsync(principalId);

        var linked = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ContactService>().EnsureSelfContactAsync(principalId);
        }));

        var id = Assert.Single(linked.Distinct());
        Assert.Equal(id, Assert.Single(await ContactsInAsync(bob, personal)).Id);
        Assert.Equal(id, await MyContactIdAsync(bob));
    }

    [Fact]
    public async Task A_linked_contact_cannot_be_deleted_by_any_writer_and_an_unlinked_one_can()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var family = await CreateAddressBookAsync(alice, "family");
        var card = await CreateContactAsync(alice, family, "Bob", "Builder", email: Bob);
        var other = await CreateContactAsync(alice, family, "Jane", "Doe");
        await GrantAsync(alice, family, Bob, "read-write");
        Assert.Equal(card.Id, await MyContactIdAsync(bob));

        foreach (var api in new[] { alice, bob })
        {
            var resp = await api.DeleteAsync($"/contacts/{card.Id}");
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("This is Bob Builder's own contact — it can't be deleted.", body.RootElement.GetProperty("detail").GetString());
        }

        (await alice.GetAsync($"/contacts/{card.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/contacts/{other.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_replayed_delete_of_a_linked_contact_is_refused_again()
    {
        var bob = Factory.ApiClient(Bob);
        await BootstrapAsync(bob);
        var self = (await MyContactIdAsync(bob))!.Value;
        var key = Guid.NewGuid();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"/contacts/{self}");
            req.Headers.Add("Idempotency-Key", key.ToString());
            Assert.Equal(HttpStatusCode.Conflict, (await bob.SendAsync(req)).StatusCode);
        }

        (await bob.GetAsync($"/contacts/{self}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Mcp_delete_refuses_a_linked_contact_and_deletes_an_unlinked_one()
    {
        var bob = Factory.ApiClient(Bob);
        var personal = await BootstrapAsync(bob);
        var self = (await MyContactIdAsync(bob))!.Value;
        var other = await CreateContactAsync(bob, personal, "Jane");
        await using var mcp = await McpAsync(Bob);

        var refused = await mcp.CallToolAsync("delete_contact", new Dictionary<string, object?> { ["contactId"] = self });
        Assert.True(refused.IsError);
        Assert.Contains("This is bob's own contact — it can't be deleted.", Assert.IsType<TextContentBlock>(Assert.Single(refused.Content)).Text);
        (await bob.GetAsync($"/contacts/{self}")).EnsureSuccessStatusCode();

        var deleted = await mcp.CallToolAsync("delete_contact", new Dictionary<string, object?> { ["contactId"] = other.Id });
        Assert.True(deleted.IsError != true);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/contacts/{other.Id}")).StatusCode);
    }
}
