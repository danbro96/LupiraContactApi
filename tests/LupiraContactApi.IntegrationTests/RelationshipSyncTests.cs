using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Relationships;
using LupiraContactApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Relationships for clients: the changes feed (full sync, deltas, tombstones for removals and for contacts
/// that leave view) and the visible listing — both limited to relationships whose two contacts the caller can read.</summary>
public sealed class RelationshipSyncTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static async Task<RelationshipChangesResponse> ChangesAsync(HttpClient api, string? since = null)
    {
        var resp = await api.GetAsync("/sync/relationships" + (since is null ? "" : $"?since={Uri.EscapeDataString(since)}"));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RelationshipChangesResponse>(Json))!;
    }

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
}
