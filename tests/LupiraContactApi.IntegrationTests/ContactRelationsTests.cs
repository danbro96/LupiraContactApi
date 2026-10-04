using System.Net;
using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Contact-to-contact relationships over REST: one view whichever side stores them, per-side labels, upsert/end/remove
/// from either side, the read-both/write-what-you-change authorization rule, and read-side filtering of dangling edges.</summary>
public sealed class ContactRelationsTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static Task<HttpResponseMessage> PostRelationAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind, string? label = null, DateOnly? since = null) =>
        api.PostAsJsonAsync($"/contacts/{contactId}/relations", new AddContactRelationRequest { ToContactId = toContactId, Kind = kind, Label = label, Since = since });

    static async Task<ContactRelationEntryDto> AddRelationAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind, string? label = null, DateOnly? since = null)
    {
        var resp = await PostRelationAsync(api, contactId, toContactId, kind, label, since);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactRelationEntryDto>())!;
    }

    static async Task<List<ContactRelationEntryDto>> RelationsAsync(HttpClient api, Guid id) =>
        (await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{id}/relations"))!;

    static async Task<ContactDto> RawAsync(HttpClient api, Guid id) => (await api.GetFromJsonAsync<ContactDto>($"/contacts/{id}"))!;

    static async Task<string> RelationshipCursorAsync(HttpClient api) =>
        (await api.GetFromJsonAsync<RelationshipChangesResponse>("/sync/relationships"))!.Cursor;

    [Fact]
    public async Task A_relationship_reads_the_same_from_both_sides()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var y = await CreateContactAsync(api, abId, "Young", "Doe");
        var x = await CreateContactAsync(api, abId, "Old", "Doe");

        var added = await AddRelationAsync(api, y.Id, x.Id, ContactRelationKind.Parent, "dad", new DateOnly(1990, 1, 1));
        Assert.Equal((x.Id, ContactRelationKind.Parent, "dad"), (added.ContactId, added.Kind, added.Label));
        Assert.Equal(y.Etag, (await RawAsync(api, y.Id)).Etag);   // a relationship is its own record, not contact content

        var fromY = Assert.Single(await RelationsAsync(api, y.Id));
        Assert.Equal((x.Id, ContactRelationKind.Parent, "dad", new DateOnly(1990, 1, 1)), (fromY.ContactId, fromY.Kind, fromY.Label, fromY.Since));

        // Since belongs to the relationship; the label is Y's name for X, so X's side has none until X gives one.
        var fromX = Assert.Single(await RelationsAsync(api, x.Id));
        Assert.Equal((y.Id, ContactRelationKind.Child, null, new DateOnly(1990, 1, 1)), (fromX.ContactId, fromX.Kind, fromX.Label, fromX.Since));
    }

    [Fact]
    public async Task Each_side_keeps_its_own_label_and_the_relationship_lists_once()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var y = await CreateContactAsync(api, abId, "Young", "Doe");
        var x = await CreateContactAsync(api, abId, "Old", "Doe");

        await AddRelationAsync(api, y.Id, x.Id, ContactRelationKind.Parent, "dad");
        var fromX = await AddRelationAsync(api, x.Id, y.Id, ContactRelationKind.Child, "son");
        Assert.Equal("son", fromX.Label);

        Assert.Equal("dad", Assert.Single(await RelationsAsync(api, y.Id)).Label);
        Assert.Equal("son", Assert.Single(await RelationsAsync(api, x.Id)).Label);
    }

    [Fact]
    public async Task An_edit_from_the_other_side_keeps_each_sides_label()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Friend, "bestie");

        var revised = await AddRelationAsync(api, b.Id, a.Id, ContactRelationKind.Friend, since: new DateOnly(2001, 9, 1));
        Assert.Equal(new DateOnly(2001, 9, 1), revised.Since);

        Assert.Null(revised.Label);
        var fromA = Assert.Single(await RelationsAsync(api, a.Id));
        Assert.Equal(("bestie", new DateOnly(2001, 9, 1)), (fromA.Label, fromA.Since));
    }

    [Fact]
    public async Task Readd_is_idempotent_and_a_new_label_upserts()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");

        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Parent, "dad");
        var cursor = await RelationshipCursorAsync(api);
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Parent, "dad");
        Assert.Equal(cursor, await RelationshipCursorAsync(api));   // no event appended

        var relabeled = await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Parent, "father");
        Assert.Equal("father", relabeled.Label);
        Assert.NotEqual(cursor, await RelationshipCursorAsync(api));
    }

    [Fact]
    public async Task Remove_from_either_side_erases_every_copy_and_a_second_delete_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Friend, "pal");
        await AddRelationAsync(api, b.Id, a.Id, ContactRelationKind.Friend, "buddy");
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Colleague);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/contacts/{b.Id}/relations/{a.Id}?kind=Friend")).StatusCode);

        Assert.Equal(ContactRelationKind.Colleague, Assert.Single(await RelationsAsync(api, a.Id)).Kind);
        Assert.Equal(ContactRelationKind.Colleague, Assert.Single(await RelationsAsync(api, b.Id)).Kind);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/contacts/{a.Id}/relations/{b.Id}?kind=Friend")).StatusCode);
    }

    [Fact]
    public async Task Self_relation_and_unknown_target_are_bad_requests()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostRelationAsync(api, a.Id, a.Id, ContactRelationKind.Friend)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRelationAsync(api, a.Id, Guid.NewGuid(), ContactRelationKind.Friend)).StatusCode);

        var b = await CreateContactAsync(api, abId, "B", "Gone");
        await api.DeleteAsync($"/contacts/{b.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRelationAsync(api, a.Id, b.Id, ContactRelationKind.Friend)).StatusCode);
    }

    [Fact]
    public async Task Read_is_required_on_both_sides_and_write_on_each_copy_changed()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var x = await CreateContactAsync(alice, aliceBook, "X", "Alice");
        var y = await CreateContactAsync(bob, bobBook, "Y", "Bob");

        // Alice has no access to Bob's book: she can't relate to his contact.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostRelationAsync(alice, x.Id, y.Id, ContactRelationKind.Friend)).StatusCode);

        // Bob can read (not write) Alice's book. Write on his own contact's book is enough to relate the two, from either card...
        await alice.PostAsJsonAsync($"/address-books/{aliceBook}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" });
        Assert.Equal(y.Id, (await AddRelationAsync(bob, x.Id, y.Id, ContactRelationKind.Friend)).ContactId);

        // ...but a label is her contact's own word for his, so it needs write on her book.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostRelationAsync(bob, x.Id, y.Id, ContactRelationKind.Friend, "pal")).StatusCode);
    }

    [Fact]
    public async Task Listing_omits_relationships_with_contacts_the_viewer_cannot_read()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var x = await CreateContactAsync(alice, aliceBook, "X", "Alice");
        var z = await CreateContactAsync(bob, bobBook, "Z", "Bob");

        await alice.PostAsJsonAsync($"/address-books/{aliceBook}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" });
        await AddRelationAsync(bob, z.Id, x.Id, ContactRelationKind.Colleague);

        Assert.Contains(await RelationsAsync(bob, x.Id), e => e.ContactId == z.Id);
        Assert.Empty(await RelationsAsync(alice, x.Id));
    }

    [Fact]
    public async Task Deleted_contact_hides_the_relationship_until_it_is_restored()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Sibling);

        await api.DeleteAsync($"/contacts/{b.Id}");

        Assert.Empty(await RelationsAsync(api, a.Id));

        // No-FK convention: the relationship was kept, only filtered — a re-import of the same uid brings it back.
        (await PutVcfAsync(api, Email, abId, b.ExternalId, MinimalVcf(b.ExternalId, "B Two"))).EnsureSuccessStatusCode();
        Assert.Equal(b.Id, Assert.Single(await RelationsAsync(api, a.Id)).ContactId);
    }
}
