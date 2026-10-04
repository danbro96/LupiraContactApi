using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Moving contacts between address books: the id survives, both books must be writable, and the sync feed tombstones
/// the contact to the old book's readers while delivering it to the new book's.</summary>
public sealed class ContactMoveTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Alice = "alice@x.test";
    const string Bob = "bob@x.test";
    const string Carol = "carol@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static Task<HttpResponseMessage> MoveAsync(HttpClient api, Guid contactId, Guid addressBookId) =>
        api.PostAsJsonAsync($"/contacts/{contactId}/move", new MoveContactRequest { AddressBookId = addressBookId }, Json);

    static async Task<SyncChangesResponse> ChangesAsync(HttpClient api, string? since = null) =>
        (await api.GetFromJsonAsync<SyncChangesResponse>("/sync/changes" + (since is null ? "" : $"?since={since}"), Json))!;

    [Fact]
    public async Task Move_keeps_the_id_relations_and_etag()
    {
        var api = Factory.ApiClient(Alice);
        var from = await CreateAddressBookAsync(api, "from");
        var to = await CreateAddressBookAsync(api, "to");
        var contact = await CreateContactAsync(api, from, "Jane");
        var friend = await CreateContactAsync(api, from, "John");
        (await api.PostAsJsonAsync($"/contacts/{friend.Id}/relations", new AddContactRelationRequest { ToContactId = contact.Id, Kind = ContactRelationKind.Friend }, Json))
            .EnsureSuccessStatusCode();
        var before = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}", Json))!;

        var resp = await MoveAsync(api, contact.Id, to);
        resp.EnsureSuccessStatusCode();
        var moved = (await resp.Content.ReadFromJsonAsync<ContactDto>(Json))!;

        Assert.Equal((contact.Id, to, before.Etag, before.Version + 1), (moved.Id, moved.AddressBookId, moved.Etag, moved.Version));
        Assert.DoesNotContain(await api.GetFromJsonAsync<List<ContactDto>>($"/contacts?addressBookId={from}", Json) ?? [], c => c.Id == contact.Id);
        Assert.Contains(await api.GetFromJsonAsync<List<ContactDto>>($"/contacts?addressBookId={to}", Json) ?? [], c => c.Id == contact.Id);
        var relations = await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{friend.Id}/relations", Json);
        Assert.Contains(relations!, r => r.ContactId == contact.Id && r.Kind == ContactRelationKind.Friend);
    }

    [Fact]
    public async Task Move_needs_write_access_on_both_books()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var alicesBook = await CreateAddressBookAsync(alice, "alices");
        var bobsBook = await CreateAddressBookAsync(bob, "bobs");
        var contact = await CreateContactAsync(alice, alicesBook);

        await GrantAsync(alice, alicesBook, Bob, "read");
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(bob, contact.Id, bobsBook)).StatusCode);   // source read-only
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(alice, contact.Id, bobsBook)).StatusCode);   // target unshared
        await GrantAsync(bob, bobsBook, Alice, "read");
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(alice, contact.Id, bobsBook)).StatusCode);   // target read-only
        Assert.Equal(alicesBook, (await alice.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}", Json))!.AddressBookId);

        await GrantAsync(alice, alicesBook, Bob, "read-write");
        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(bob, contact.Id, bobsBook)).StatusCode);
    }

    [Fact]
    public async Task Unknown_or_deleted_contact_and_unknown_book_are_404_and_an_empty_book_400()
    {
        var api = Factory.ApiClient(Alice);
        var book = await CreateAddressBookAsync(api, "from");
        var other = await CreateAddressBookAsync(api, "to");
        var contact = await CreateContactAsync(api, book);

        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(api, Guid.NewGuid(), other)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(api, contact.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(api, contact.Id, Guid.Empty)).StatusCode);
        (await api.DeleteAsync($"/contacts/{contact.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(api, contact.Id, other)).StatusCode);
    }

    [Fact]
    public async Task Moving_to_the_current_book_is_a_no_op()
    {
        var api = Factory.ApiClient(Alice);
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book);

        var resp = await MoveAsync(api, contact.Id, book);
        resp.EnsureSuccessStatusCode();
        var same = (await resp.Content.ReadFromJsonAsync<ContactDto>(Json))!;
        Assert.Equal((book, contact.Version), (same.AddressBookId, same.Version));
    }

    [Fact]
    public async Task Sync_feed_tombstones_it_to_the_old_books_readers_and_delivers_it_to_the_new_books()
    {
        var alice = Factory.ApiClient(Alice);
        var oldReader = Factory.ApiClient(Bob);
        var newReader = Factory.ApiClient(Carol);
        var from = await CreateAddressBookAsync(alice, "from");
        var to = await CreateAddressBookAsync(alice, "to");
        var contact = await CreateContactAsync(alice, from);
        // Grant first: a grant changes the reader's cursor scope, which restarts its stream.
        await GrantAsync(alice, from, Bob, "read");
        await GrantAsync(alice, to, Carol, "read");
        var oldStart = await ChangesAsync(oldReader);
        var newStart = await ChangesAsync(newReader);
        Assert.Contains(oldStart.Changed, c => c.Contact.Id == contact.Id);
        Assert.Empty(newStart.Changed);

        (await MoveAsync(alice, contact.Id, to)).EnsureSuccessStatusCode();

        var oldDelta = await ChangesAsync(oldReader, oldStart.Cursor);
        Assert.False(oldDelta.Reset);
        Assert.Empty(oldDelta.Changed);
        Assert.Equal([contact.Id], oldDelta.Deleted);

        var newDelta = await ChangesAsync(newReader, newStart.Cursor);
        Assert.False(newDelta.Reset);
        Assert.Empty(newDelta.Deleted);
        var arrived = Assert.Single(newDelta.Changed).Contact;
        Assert.Equal((contact.Id, to), (arrived.Id, arrived.AddressBookId));
    }

    [Fact]
    public async Task Batch_move_over_mcp_reports_each_contact()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var from = await CreateAddressBookAsync(alice, "from");
        var to = await CreateAddressBookAsync(alice, "to");
        var movable = await CreateContactAsync(alice, from, "Move");
        var already = await CreateContactAsync(alice, to, "Stay");
        var foreign = await CreateContactAsync(bob, await CreateAddressBookAsync(bob, "bobs"), "Foreign");
        var missing = Guid.NewGuid();

        await using var mcp = await McpAsync(Alice);
        var result = await mcp.CallToolAsync("move_contacts", new Dictionary<string, object?>
        {
            ["contactIds"] = new[] { movable.Id, already.Id, foreign.Id, missing, movable.Id },
            ["addressBookId"] = to,
        });

        var outcomes = ToolResult<List<ContactMoveResult>>(result);
        Assert.Equal(
            [(movable.Id, ContactMoveOutcome.Moved), (already.Id, ContactMoveOutcome.Unchanged), (foreign.Id, ContactMoveOutcome.Forbidden), (missing, ContactMoveOutcome.NotFound)],
            outcomes.Select(o => (o.ContactId, o.Outcome)));
        Assert.Equal(to, (await alice.GetFromJsonAsync<ContactDto>($"/contacts/{movable.Id}", Json))!.AddressBookId);
    }

    [Fact]
    public async Task Batch_move_over_mcp_is_capped_at_500()
    {
        var api = Factory.ApiClient(Alice);
        var to = await CreateAddressBookAsync(api);

        await using var mcp = await McpAsync(Alice);
        var result = await mcp.CallToolAsync("move_contacts", new Dictionary<string, object?>
        {
            ["contactIds"] = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray(),
            ["addressBookId"] = to,
        });

        Assert.True(result.IsError);
        Assert.Contains("At most 500", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }
}
