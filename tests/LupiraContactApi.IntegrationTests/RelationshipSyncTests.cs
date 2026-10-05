using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Sync;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Relationships;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Relationships for clients: the changes feed (paged full sync and deltas, tombstones for removals and for
/// contacts that leave view, a reset on revoke) and the visible listing — both limited to relationships whose two
/// contacts the caller can read.</summary>
public sealed class RelationshipSyncTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static async Task<SyncPage<RelationshipDto>> ChangesAsync(HttpClient api, string? since = null, int? limit = null)
    {
        var qs = new List<string>();
        if (since is not null) qs.Add($"since={Uri.EscapeDataString(since)}");
        if (limit is not null) qs.Add($"limit={limit}");
        var resp = await api.GetAsync("/sync/relationships" + (qs.Count > 0 ? "?" + string.Join("&", qs) : ""));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SyncPage<RelationshipDto>>(Json))!;
    }

    static async Task<(List<Guid> Seen, string Cursor, int Pages)> DrainAsync(HttpClient api, string? since, int limit)
    {
        var seen = new List<Guid>();
        SyncPage<RelationshipDto> page;
        var pages = 0;
        do
        {
            page = await ChangesAsync(api, since, limit);
            seen.AddRange(page.Changed.Select(r => r.Id));
            since = page.Cursor;
            Assert.True(++pages < 20, "paging loop did not terminate");
        } while (page.HasMore);

        return (seen, since, pages);
    }

    static async Task<HashSet<Guid>> VisibleIdsAsync(HttpClient api) =>
        [.. (await api.GetFromJsonAsync<List<RelationshipDto>>("/relationships", Json))!.Select(r => r.Id)];

    static async Task<ContactRelationEntryDto> RelateAsync(HttpClient api, Guid id, Guid toContactId, ContactRelationKind kind, string? label = null)
    {
        var resp = await api.PostAsJsonAsync($"/contacts/{id}/relations", new AddContactRelationRequest { ToContactId = toContactId, Kind = kind, Label = label });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactRelationEntryDto>(Json))!;
    }

    [Fact]
    public async Task Full_sync_then_deltas_carry_changes_and_removals()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var child = await CreateContactAsync(api, ab, "Cara", "Child");
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");
        var friend = await CreateContactAsync(api, ab, "Fay", "Friend");
        await RelateAsync(api, child.Id, parent.Id, ContactRelationKind.Parent, "dad");

        var full = await ChangesAsync(api);
        Assert.True(full.Reset);
        var r = Assert.Single(full.Changed);
        var (low, high) = string.CompareOrdinal(child.Id.ToString(), parent.Id.ToString()) < 0 ? (child.Id, parent.Id) : (parent.Id, child.Id);
        Assert.Equal((low, high), (r.LowId, r.HighId));
        Assert.Equal(low == child.Id ? "dad" : null, r.LabelFromLow);

        await RelateAsync(api, child.Id, friend.Id, ContactRelationKind.Friend);
        var delta = await ChangesAsync(api, full.Cursor);
        Assert.False(delta.Reset);
        Assert.Contains(delta.Changed, x => x.Kind == ContactRelationKind.Friend);
        Assert.Empty(delta.Deleted);

        (await api.DeleteAsync($"/contacts/{child.Id}/relations/{friend.Id}?kind=Friend")).EnsureSuccessStatusCode();
        var afterRemove = await ChangesAsync(api, delta.Cursor);
        var removed = Assert.Single(afterRemove.Deleted);
        Assert.DoesNotContain(afterRemove.Changed, x => x.Id == removed);
    }

    [Fact]
    public async Task Deleting_a_contact_tombstones_its_relationships()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, ab, "Ann", "A");
        var b = await CreateContactAsync(api, ab, "Bo", "B");
        await RelateAsync(api, a.Id, b.Id, ContactRelationKind.Sibling);
        var full = await ChangesAsync(api);
        var id = Assert.Single(full.Changed).Id;

        await api.DeleteAsync($"/contacts/{b.Id}");

        Assert.Equal(id, Assert.Single((await ChangesAsync(api, full.Cursor)).Deleted));
    }

    [Fact]
    public async Task Only_relationships_with_both_contacts_readable_are_listed()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var x = await CreateContactAsync(alice, aliceBook, "X", "Alice");
        var y = await CreateContactAsync(bob, bobBook, "Y", "Bob");
        await alice.PostAsJsonAsync($"/address-books/{aliceBook}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" });
        await RelateAsync(bob, y.Id, x.Id, ContactRelationKind.Colleague);

        Assert.Single((await ChangesAsync(bob)).Changed);
        Assert.Single((await bob.GetFromJsonAsync<List<RelationshipDto>>("/relationships", Json))!);
        Assert.Empty((await ChangesAsync(alice)).Changed);
        Assert.Empty((await alice.GetFromJsonAsync<List<RelationshipDto>>("/relationships", Json))!);
    }

    [Fact]
    public async Task Full_sync_and_deltas_page_across_more_relationships_than_the_limit()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var hub = await CreateContactAsync(api, ab, "Hub", "H");
        for (var n = 0; n < 5; n++)
            await RelateAsync(api, hub.Id, (await CreateContactAsync(api, ab, $"F{n}", "F")).Id, ContactRelationKind.Friend);
        var first = await VisibleIdsAsync(api);

        var full = await DrainAsync(api, null, 2);
        Assert.Equal(3, full.Pages);
        Assert.Equal(first.Count, full.Seen.Count);
        Assert.Equal(first, full.Seen.ToHashSet());

        for (var n = 0; n < 5; n++)
            await RelateAsync(api, hub.Id, (await CreateContactAsync(api, ab, $"C{n}", "C")).Id, ContactRelationKind.Colleague);
        var second = (await VisibleIdsAsync(api)).Except(first).ToHashSet();

        var delta = await DrainAsync(api, full.Cursor, 2);
        Assert.True(delta.Pages >= 3);
        Assert.Superset(second, delta.Seen.ToHashSet());
    }

    [Fact]
    public async Task A_contact_moved_to_an_unreadable_book_tombstones_its_relationships()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var shared = await CreateAddressBookAsync(alice, "shared");
        var mine = await CreateAddressBookAsync(alice, "mine");
        await GrantAsync(alice, shared, "bob@x.test", "read");
        var a = await CreateContactAsync(alice, shared, "Ann", "A");
        var b = await CreateContactAsync(alice, shared, "Bo", "B");
        await RelateAsync(alice, a.Id, b.Id, ContactRelationKind.Sibling);
        var full = await ChangesAsync(bob);
        var id = Assert.Single(full.Changed).Id;

        (await alice.PostAsJsonAsync($"/contacts/{b.Id}/move", new MoveContactRequest { AddressBookId = mine }, Json)).EnsureSuccessStatusCode();

        var delta = await ChangesAsync(bob, full.Cursor);
        Assert.False(delta.Reset);
        Assert.Empty(delta.Changed);
        Assert.Equal(id, Assert.Single(delta.Deleted));
    }

    [Fact]
    public async Task Revoking_access_restarts_the_feed()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var shared = await CreateAddressBookAsync(alice, "shared");
        await GrantAsync(alice, shared, "bob@x.test", "read");
        var a = await CreateContactAsync(alice, shared, "Ann", "A");
        var b = await CreateContactAsync(alice, shared, "Bo", "B");
        await RelateAsync(alice, a.Id, b.Id, ContactRelationKind.Sibling);
        var full = await ChangesAsync(bob);
        Assert.Single(full.Changed);

        (await alice.DeleteAsync($"/address-books/{shared}/owners?email=bob@x.test")).EnsureSuccessStatusCode();

        var after = await ChangesAsync(bob, full.Cursor);
        Assert.True(after.Reset);
        Assert.Empty(after.Changed);
        Assert.Empty(after.Deleted);
    }

    [Fact]
    public async Task Another_users_relationships_never_reach_my_tombstones()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var (a1, a2) = (await CreateContactAsync(alice, aliceBook, "Ann", "A"), await CreateContactAsync(alice, aliceBook, "Al", "A"));
        await RelateAsync(alice, a1.Id, a2.Id, ContactRelationKind.Sibling);
        var full = await ChangesAsync(alice);
        var mine = Assert.Single(full.Changed).Id;

        var (b1, b2, b3) = (await CreateContactAsync(bob, bobBook, "Bo", "B"), await CreateContactAsync(bob, bobBook, "Bea", "B"), await CreateContactAsync(bob, bobBook, "Ben", "B"));
        await RelateAsync(bob, b1.Id, b2.Id, ContactRelationKind.Friend);
        await RelateAsync(bob, b1.Id, b3.Id, ContactRelationKind.Colleague);
        (await bob.DeleteAsync($"/contacts/{b1.Id}/relations/{b2.Id}?kind=Friend")).EnsureSuccessStatusCode();
        (await bob.DeleteAsync($"/contacts/{b3.Id}")).EnsureSuccessStatusCode();
        (await alice.DeleteAsync($"/contacts/{a2.Id}")).EnsureSuccessStatusCode();

        var delta = await ChangesAsync(alice, full.Cursor);
        Assert.Empty(delta.Changed);
        Assert.Equal(mine, Assert.Single(delta.Deleted));
    }
}
