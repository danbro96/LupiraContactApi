using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Sync;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The residencies feed and listing: paged full sync and deltas, tombstones for removals and for contacts that
/// leave view, limited to contacts the caller can read.</summary>
public sealed class ResidencySyncTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static async Task<SyncPage<ResidencyDto>> ChangesAsync(HttpClient api, string? since = null, int? limit = null)
    {
        var qs = new List<string>();
        if (since is not null) qs.Add($"since={Uri.EscapeDataString(since)}");
        if (limit is not null) qs.Add($"limit={limit}");
        return (await api.GetFromJsonAsync<SyncPage<ResidencyDto>>("/sync/residencies" + (qs.Count > 0 ? "?" + string.Join("&", qs) : ""), Json))!;
    }

    static async Task<(List<Guid> Seen, string Cursor, int Pages)> DrainAsync(HttpClient api, string? since, int limit)
    {
        var seen = new List<Guid>();
        SyncPage<ResidencyDto> page;
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

    [Fact]
    public async Task Full_sync_then_deltas_carry_changes_and_removals()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var home = await AddResidencyAsync(api, c.Id, Guid.NewGuid());

        var full = await ChangesAsync(api);
        Assert.True(full.Reset);
        Assert.Equal(home.Id, Assert.Single(full.Changed).Id);

        var work = await AddResidencyAsync(api, c.Id, Guid.NewGuid(), Core.Domain.Shared.ContactAddressType.Work);
        var delta = await ChangesAsync(api, full.Cursor);
        Assert.Contains(delta.Changed, r => r.Id == work.Id);

        (await api.DeleteAsync($"/residencies/{work.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(work.Id, Assert.Single((await ChangesAsync(api, delta.Cursor)).Deleted));
    }

    [Fact]
    public async Task Deleting_a_contact_tombstones_its_residencies()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var home = await AddResidencyAsync(api, c.Id, Guid.NewGuid());
        var full = await ChangesAsync(api);

        await api.DeleteAsync($"/contacts/{c.Id}");

        Assert.Equal(home.Id, Assert.Single((await ChangesAsync(api, full.Cursor)).Deleted));
        Assert.Empty((await api.GetFromJsonAsync<List<ResidencyDto>>("/residencies", Json))!);
    }

    [Fact]
    public async Task Only_readable_contacts_residencies_are_listed()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var c = await CreateContactAsync(alice, await CreateAddressBookAsync(alice));
        await AddResidencyAsync(alice, c.Id, Guid.NewGuid());

        Assert.Single((await alice.GetFromJsonAsync<List<ResidencyDto>>("/residencies", Json))!);
        Assert.Empty((await bob.GetFromJsonAsync<List<ResidencyDto>>("/residencies", Json))!);
        Assert.Empty((await ChangesAsync(bob)).Changed);
    }

    [Fact]
    public async Task Full_sync_and_deltas_page_across_more_residencies_than_the_limit()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var first = new HashSet<Guid>();
        for (var n = 0; n < 5; n++) first.Add((await AddResidencyAsync(api, c.Id, Guid.NewGuid(), Core.Domain.Shared.ContactAddressType.Work)).Id);

        var full = await DrainAsync(api, null, 2);
        Assert.Equal(3, full.Pages);
        Assert.Equal(first.Count, full.Seen.Count);
        Assert.Equal(first, full.Seen.ToHashSet());

        var second = new HashSet<Guid>();
        for (var n = 0; n < 5; n++) second.Add((await AddResidencyAsync(api, c.Id, Guid.NewGuid(), Core.Domain.Shared.ContactAddressType.Work)).Id);

        var delta = await DrainAsync(api, full.Cursor, 2);
        Assert.True(delta.Pages >= 3);
        Assert.Superset(second, delta.Seen.ToHashSet());
    }

    [Fact]
    public async Task A_contact_moved_to_an_unreadable_book_tombstones_its_residencies()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var shared = await CreateAddressBookAsync(alice, "shared");
        var mine = await CreateAddressBookAsync(alice, "mine");
        await GrantAsync(alice, shared, "bob@x.test", "read");
        var c = await CreateContactAsync(alice, shared);
        var home = await AddResidencyAsync(alice, c.Id, Guid.NewGuid());
        var full = await ChangesAsync(bob);
        Assert.Equal(home.Id, Assert.Single(full.Changed).Id);

        (await alice.PostAsJsonAsync($"/contacts/{c.Id}/move", new MoveContactRequest { AddressBookId = mine }, Json)).EnsureSuccessStatusCode();

        var delta = await ChangesAsync(bob, full.Cursor);
        Assert.Empty(delta.Changed);
        Assert.Equal(home.Id, Assert.Single(delta.Deleted));
    }

    [Fact]
    public async Task Another_users_residencies_never_reach_my_tombstones()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var mine = await CreateContactAsync(alice, await CreateAddressBookAsync(alice));
        var home = await AddResidencyAsync(alice, mine.Id, Guid.NewGuid());
        var full = await ChangesAsync(alice);

        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var (b1, b2) = (await CreateContactAsync(bob, bobBook, "Bo", "B"), await CreateContactAsync(bob, bobBook, "Bea", "B"));
        var removed = await AddResidencyAsync(bob, b1.Id, Guid.NewGuid());
        await AddResidencyAsync(bob, b2.Id, Guid.NewGuid());
        (await bob.DeleteAsync($"/residencies/{removed.Id}")).EnsureSuccessStatusCode();
        (await bob.DeleteAsync($"/contacts/{b2.Id}")).EnsureSuccessStatusCode();
        (await alice.DeleteAsync($"/residencies/{home.Id}")).EnsureSuccessStatusCode();

        var delta = await ChangesAsync(alice, full.Cursor);
        Assert.Empty(delta.Changed);
        Assert.Equal(home.Id, Assert.Single(delta.Deleted));
    }
}
