using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Sync;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The offline-client sync surface end to end: the delta loop (create → revise → delete), full-sync and
/// delta paging, cursor scope (foreign churn, grant/revoke restarts, moves out of a readable book), the
/// <c>/sync/changes</c> alias, the address-book and group snapshots, guard exposure, Idempotency-Key replays,
/// occurredAt LWW over REST, and SourceKey create dedup.</summary>
public class SyncEndpointsTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static string FeedUrl(string path, string? since, int? limit)
    {
        var qs = new List<string>();
        if (since is not null) qs.Add($"since={since}");
        if (limit is not null) qs.Add($"limit={limit}");
        return path + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
    }

    async Task<SyncPage<ContactSyncChange>> ChangesAsync(HttpClient api, string? since = null, int? limit = null)
    {
        var resp = await api.GetAsync(FeedUrl("/sync/contacts", since, limit));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SyncPage<ContactSyncChange>>(Json))!;
    }

    [Fact]
    public async Task Delta_loop_sees_create_revise_and_delete()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);

        var start = await ChangesAsync(api);
        var contact = await CreateContactAsync(api, book, "Jane");

        var afterCreate = await ChangesAsync(api, start.Cursor);
        var entry = Assert.Single(afterCreate.Changed, c => c.Contact.Id == contact.Id);
        Assert.True(entry.Contact.Version >= 1);
        Assert.NotEqual(default, entry.Guards.Core.Ts);

        (await api.PutAsJsonAsync($"/contacts/{contact.Id}", new ReviseContactRequest { GivenName = "Janet" }, Json)).EnsureSuccessStatusCode();
        var afterRevise = await ChangesAsync(api, afterCreate.Cursor);
        Assert.Equal("Janet", Assert.Single(afterRevise.Changed, c => c.Contact.Id == contact.Id).Contact.GivenName);

        (await api.DeleteAsync($"/contacts/{contact.Id}")).EnsureSuccessStatusCode();
        var afterDelete = await ChangesAsync(api, afterRevise.Cursor);
        Assert.Contains(contact.Id, afterDelete.Deleted);
        Assert.DoesNotContain(afterDelete.Changed, c => c.Contact.Id == contact.Id);

        var quiet = await ChangesAsync(api, afterDelete.Cursor);
        Assert.Empty(quiet.Changed);
        Assert.Empty(quiet.Deleted);
        Assert.Equal(afterDelete.Cursor, quiet.Cursor);
    }

    [Fact]
    public async Task Full_sync_pages_and_covers_all_live_contacts()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var live = new HashSet<Guid>();
        for (var n = 0; n < 5; n++) live.Add((await CreateContactAsync(api, book, $"Person{n}")).Id);
        var doomed = await CreateContactAsync(api, book, "Doomed");
        (await api.DeleteAsync($"/contacts/{doomed.Id}")).EnsureSuccessStatusCode();

        var seen = new List<Guid>();
        string? cursor = null;
        SyncPage<ContactSyncChange> page;
        var pages = 0;
        do
        {
            page = await ChangesAsync(api, cursor, limit: 2);
            Assert.Equal(pages == 0, page.Reset);
            Assert.Empty(page.Deleted);
            seen.AddRange(page.Changed.Select(c => c.Contact.Id));
            cursor = page.Cursor;
            Assert.True(++pages < 20, "paging loop did not terminate");
        } while (page.HasMore);

        Assert.Equal(3, pages);
        Assert.Equal(live, seen.ToHashSet());
        Assert.Equal(seen.Count, seen.Distinct().Count());

        var next = await ChangesAsync(api, cursor);
        Assert.False(next.Reset);
        Assert.Empty(next.Changed);
        Assert.Empty(next.Deleted);
    }

    [Fact]
    public async Task Delta_pages_across_more_changes_than_the_limit()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var start = await ChangesAsync(api);
        var created = new HashSet<Guid>();
        for (var n = 0; n < 5; n++) created.Add((await CreateContactAsync(api, book, $"Person{n}")).Id);

        var seen = new HashSet<Guid>();
        var cursor = start.Cursor;
        SyncPage<ContactSyncChange> page;
        var pages = 0;
        do
        {
            page = await ChangesAsync(api, cursor, limit: 2);
            Assert.False(page.Reset);
            seen.UnionWith(page.Changed.Select(c => c.Contact.Id));
            cursor = page.Cursor;
            Assert.True(++pages < 20, "paging loop did not terminate");
        } while (page.HasMore);

        Assert.True(pages >= 3);
        Assert.Equal(created, seen);
    }

    [Fact]
    public async Task A_cursor_without_a_resume_id_resumes_as_a_delta()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book, "Jane");

        var full = await ChangesAsync(api);
        var parts = full.Cursor.Split('.');
        Assert.Equal(2, parts.Length);

        var fromStart = await ChangesAsync(api, $"0.{parts[1]}");
        Assert.False(fromStart.Reset);
        Assert.Contains(fromStart.Changed, c => c.Contact.Id == contact.Id);
    }

    [Fact]
    public async Task The_changes_alias_answers_like_the_contacts_feed()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        await CreateContactAsync(api, book, "Jane");
        await CreateContactAsync(api, book, "John");

        var contacts = await api.GetStringAsync(FeedUrl("/sync/contacts", null, 1));
        var alias = await api.GetStringAsync(FeedUrl("/sync/changes", null, 1));
        Assert.Equal(contacts, alias);

        var cursor = JsonSerializer.Deserialize<SyncPage<ContactSyncChange>>(contacts, Json)!.Cursor;
        Assert.Equal(await api.GetStringAsync(FeedUrl("/sync/contacts", cursor, 1)), await api.GetStringAsync(FeedUrl("/sync/changes", cursor, 1)));
    }

    [Fact]
    public async Task Contacts_in_unreadable_books_never_leak()
    {
        var api = Factory.ApiClient("a@x");
        var stranger = Factory.ApiClient("b@x");
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book, "Private");

        var theirView = await ChangesAsync(stranger);
        Assert.DoesNotContain(theirView.Changed, c => c.Contact.Id == contact.Id);
    }

    [Fact]
    public async Task Another_callers_churn_is_neither_content_nor_tombstones()
    {
        var api = Factory.ApiClient("a@x");
        var stranger = Factory.ApiClient("b@x");
        var book = await CreateAddressBookAsync(api);
        await CreateContactAsync(api, book, "Before");

        var full = await ChangesAsync(stranger);
        Assert.Empty(full.Changed);
        Assert.False(full.HasMore);

        var contact = await CreateContactAsync(api, book, "After");
        (await api.DeleteAsync($"/contacts/{contact.Id}")).EnsureSuccessStatusCode();
        var delta = await ChangesAsync(stranger, full.Cursor);
        Assert.False(delta.Reset);
        Assert.Empty(delta.Changed);
        Assert.Empty(delta.Deleted);
    }

    [Fact]
    public async Task A_grant_restarts_the_stream_so_the_shared_book_arrives()
    {
        var api = Factory.ApiClient("a@x");
        var partner = Factory.ApiClient("b@x");
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book, "Shared");

        var before = await ChangesAsync(partner);
        Assert.True(before.Reset);
        Assert.Empty(before.Changed);

        (await api.PostAsJsonAsync($"/address-books/{book}/owners", new GrantOwnerRequest { Email = "b@x", Access = "read" })).EnsureSuccessStatusCode();
        var afterGrant = await ChangesAsync(partner, before.Cursor);
        Assert.True(afterGrant.Reset);
        Assert.Contains(afterGrant.Changed, c => c.Contact.Id == contact.Id);

        var settled = await ChangesAsync(partner, afterGrant.Cursor);
        Assert.False(settled.Reset);
        Assert.Empty(settled.Changed);

        (await api.DeleteAsync($"/address-books/{book}/owners?email=b@x")).EnsureSuccessStatusCode();
        var afterRevoke = await ChangesAsync(partner, settled.Cursor);
        Assert.True(afterRevoke.Reset);
        Assert.DoesNotContain(afterRevoke.Changed, c => c.Contact.Id == contact.Id);
    }

    [Fact]
    public async Task A_contact_moved_out_of_a_readable_book_is_tombstoned_to_its_readers()
    {
        const string uid = "moving@x";
        var api = Factory.ApiClient("a@x");
        var partner = Factory.ApiClient("b@x");
        var shared = await CreateAddressBookAsync(api, "shared");
        var first = await CreateAddressBookAsync(api, "first");
        var second = await CreateAddressBookAsync(api, "second");
        (await api.PostAsJsonAsync($"/address-books/{shared}/owners", new GrantOwnerRequest { Email = "b@x", Access = "read" })).EnsureSuccessStatusCode();
        (await PutVcfAsync(api, "a@x", shared, uid, MinimalVcf(uid, "Moving Person"))).EnsureSuccessStatusCode();

        var before = await ChangesAsync(partner);
        var id = Assert.Single(before.Changed).Contact.Id;

        // Two moves before the next pull: the book it first left must still answer for it.
        (await PutVcfAsync(api, "a@x", first, uid, MinimalVcf(uid, "Moving Person"))).EnsureSuccessStatusCode();
        (await PutVcfAsync(api, "a@x", second, uid, MinimalVcf(uid, "Moving Person"))).EnsureSuccessStatusCode();
        var delta = await ChangesAsync(partner, before.Cursor);
        Assert.False(delta.Reset);
        Assert.Empty(delta.Changed);
        Assert.Contains(id, delta.Deleted);
    }

    [Fact]
    public async Task A_bare_sequence_cursor_restarts_the_stream_once()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book, "Jane");

        var legacy = await ChangesAsync(api, "999999999");
        Assert.True(legacy.Reset);
        Assert.Contains(legacy.Changed, c => c.Contact.Id == contact.Id);
        Assert.False((await ChangesAsync(api, legacy.Cursor)).Reset);
    }

    [Fact]
    public async Task A_garbage_cursor_is_rejected()
    {
        var api = Factory.ApiClient("a@x");
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/sync/contacts?since=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/sync/changes?since=nope")).StatusCode);
    }

    [Fact]
    public async Task Replayed_revise_with_same_idempotency_key_does_not_reapply()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book, "Original");
        var key = Guid.NewGuid();

        using var first = new HttpRequestMessage(HttpMethod.Put, $"/contacts/{contact.Id}")
        { Content = JsonContent.Create(new ReviseContactRequest { GivenName = "Applied" }, options: Json) };
        first.Headers.Add("Idempotency-Key", key.ToString());
        (await api.SendAsync(first)).EnsureSuccessStatusCode();

        using var replay = new HttpRequestMessage(HttpMethod.Put, $"/contacts/{contact.Id}")
        { Content = JsonContent.Create(new ReviseContactRequest { GivenName = "Should not apply" }, options: Json) };
        replay.Headers.Add("Idempotency-Key", key.ToString());
        var replayResp = await api.SendAsync(replay);
        replayResp.EnsureSuccessStatusCode();

        var current = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}", Json))!;
        Assert.Equal("Applied", current.GivenName);
    }

    [Fact]
    public async Task Replayed_delete_with_same_idempotency_key_succeeds_instead_of_404()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book, "Doomed");
        var key = Guid.NewGuid();

        using var first = new HttpRequestMessage(HttpMethod.Delete, $"/contacts/{contact.Id}");
        first.Headers.Add("Idempotency-Key", key.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await api.SendAsync(first)).StatusCode);

        using var replay = new HttpRequestMessage(HttpMethod.Delete, $"/contacts/{contact.Id}");
        replay.Headers.Add("Idempotency-Key", key.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await api.SendAsync(replay)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/contacts/{contact.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stale_occurredAt_revise_loses_to_a_newer_write()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, book, "Original");
        var t = DateTimeOffset.UtcNow;

        (await api.PutAsJsonAsync($"/contacts/{contact.Id}", new ReviseContactRequest { GivenName = "Newer", OccurredAt = t.AddMinutes(10) }, Json)).EnsureSuccessStatusCode();
        (await api.PutAsJsonAsync($"/contacts/{contact.Id}", new ReviseContactRequest { GivenName = "Stale", OccurredAt = t.AddMinutes(5) }, Json)).EnsureSuccessStatusCode();

        var current = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}", Json))!;
        Assert.Equal("Newer", current.GivenName);
    }

    [Fact]
    public async Task SourceKey_create_is_replay_safe()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var key = $"{Guid.NewGuid():N}@mobile";

        var first = await api.PostAsJsonAsync("/contacts", new CreateContactRequest { AddressBookId = book, GivenName = "Once", SourceKey = key }, Json);
        first.EnsureSuccessStatusCode();
        var created = (await first.Content.ReadFromJsonAsync<ContactDto>(Json))!;

        var replay = await api.PostAsJsonAsync("/contacts", new CreateContactRequest { AddressBookId = book, GivenName = "Twice", SourceKey = key }, Json);
        replay.EnsureSuccessStatusCode();
        var replayed = (await replay.Content.ReadFromJsonAsync<ContactDto>(Json))!;

        Assert.Equal(created.Id, replayed.Id);
        Assert.Equal("Once", replayed.GivenName);   // idempotent hit — the second body is ignored
    }

    [Fact]
    public async Task Containers_snapshot_lists_books_and_groups()
    {
        var api = Factory.ApiClient("a@x");
        var book = await CreateAddressBookAsync(api);
        var group = await api.PostAsync($"/address-books/{book}/groups?name=Friends", null);
        group.EnsureSuccessStatusCode();

        var resp = await api.GetAsync("/sync/containers");
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<SyncContainersResponse>(Json))!;
        Assert.Contains(body.AddressBooks, b => b.Id == book);
        Assert.Contains(body.Groups, g => g.Name == "Friends");
    }

    [Fact]
    public async Task Address_book_snapshot_lists_every_readable_book()
    {
        var api = Factory.ApiClient("a@x");
        var partner = Factory.ApiClient("b@x");
        var own = await CreateAddressBookAsync(api, "own");
        var shared = await CreateAddressBookAsync(partner, "shared");
        await CreateAddressBookAsync(partner, "private");
        await GrantAsync(partner, shared, "a@x", "read");

        var snapshot = (await api.GetFromJsonAsync<SyncPage<AddressBookDto>>("/sync/address-books", Json))!;
        Assert.True(snapshot.Reset);
        Assert.False(snapshot.HasMore);
        Assert.Equal(string.Empty, snapshot.Cursor);
        Assert.Empty(snapshot.Deleted);
        Assert.Equal(new HashSet<Guid> { own, shared }, snapshot.Changed.Select(b => b.Id).ToHashSet());
    }

    [Fact]
    public async Task Group_snapshot_lists_groups_of_every_readable_book()
    {
        var api = Factory.ApiClient("a@x");
        var partner = Factory.ApiClient("b@x");
        var own = await CreateAddressBookAsync(api, "own");
        var shared = await CreateAddressBookAsync(partner, "shared");
        var hidden = await CreateAddressBookAsync(partner, "private");
        await GrantAsync(partner, shared, "a@x", "read");
        (await api.PostAsync($"/address-books/{own}/groups?name=Friends", null)).EnsureSuccessStatusCode();
        (await partner.PostAsync($"/address-books/{shared}/groups?name=Family", null)).EnsureSuccessStatusCode();
        (await partner.PostAsync($"/address-books/{hidden}/groups?name=Secret", null)).EnsureSuccessStatusCode();

        var snapshot = (await api.GetFromJsonAsync<SyncPage<ContactGroupDto>>("/sync/groups", Json))!;
        Assert.True(snapshot.Reset);
        Assert.False(snapshot.HasMore);
        Assert.Equal(string.Empty, snapshot.Cursor);
        Assert.Empty(snapshot.Deleted);
        Assert.Equal(["Family", "Friends"], snapshot.Changed.Select(g => g.Name).Order());
    }
}
