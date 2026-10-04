using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The residencies feed and listing: full sync, deltas, tombstones for removals and for contacts that leave view,
/// limited to contacts the caller can read.</summary>
public sealed class ResidencySyncTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    static async Task<ResidencyChangesResponse> ChangesAsync(HttpClient api, string? since = null) =>
        (await api.GetFromJsonAsync<ResidencyChangesResponse>("/sync/residencies" + (since is null ? "" : $"?since={Uri.EscapeDataString(since)}"), Json))!;

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
}
