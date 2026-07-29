using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.AddressBooks;
using LupiraContactApi.Dtos.Contacts;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The removable counterpart to the merge update: PUT /contacts/{id}/channels replaces reach channels
/// (emails + phones) wholesale, so an entry can be dropped (which the union-only revise cannot). Channels are
/// content-bearing → the ETag moves. Tags keep their own wholesale endpoint.</summary>
public sealed class ContactChannelsTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Alice = "alice@x.test";
    const string Bob = "bob@x.test";

    static ContactReachChannel Email(string value, string? type = null, bool preferred = false) => new(ReachMedium.Email, value, type, preferred);
    static ContactReachChannel Phone(string value, string? type = null, bool preferred = false) => new(ReachMedium.Phone, value, type, preferred);

    static Task<HttpResponseMessage> SetChannels(HttpClient api, Guid id, params ContactReachChannel[] channels) =>
        api.PutAsJsonAsync($"/contacts/{id}/channels", new SetContactChannelsRequest { Channels = [.. channels] });

    [Fact]
    public async Task Set_channels_replaces_wholesale_can_drop_and_bumps_the_etag()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, email: "jane@x.test");

        var two = await SetChannels(api, c.Id, Email("jane@x.test"), Email("jane.doe@work.test", "work"), Phone("+4670", "cell", preferred: true));
        two.EnsureSuccessStatusCode();
        var afterTwo = (await two.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal(3, afterTwo.Channels.Count);
        Assert.NotEqual(c.Etag, afterTwo.Etag);   // channels are part of the canonical content
        Assert.Contains(afterTwo.Channels, ch => ch is { Medium: ReachMedium.Phone, Type: "cell", Preferred: true });

        // Replace with a shorter list — the merge update can only add; this drops the rest.
        var one = await SetChannels(api, c.Id, Email("jane.doe@work.test"));
        var afterOne = (await one.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal("jane.doe@work.test", Assert.Single(afterOne.Channels).Value);

        var cleared = await SetChannels(api, c.Id);
        Assert.Empty((await cleared.Content.ReadFromJsonAsync<ContactDto>())!.Channels);
    }

    [Fact]
    public async Task Set_channels_trims_dedupes_and_lowercases_the_type()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var resp = await SetChannels(api, c.Id, Email("  Jane@x.test ", "HOME"), Email("jane@x.test"));
        resp.EnsureSuccessStatusCode();
        var only = Assert.Single((await resp.Content.ReadFromJsonAsync<ContactDto>())!.Channels);
        Assert.Equal("Jane@x.test", only.Value);   // first casing wins, blank trimmed, dupe dropped
        Assert.Equal("home", only.Type);           // type lowercased
    }

    [Fact]
    public async Task More_than_one_preferred_per_medium_is_rejected()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var resp = await SetChannels(api, c.Id, Phone("+461", preferred: true), Phone("+462", preferred: true));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        // One preferred per medium across two media is fine.
        var ok = await SetChannels(api, c.Id, Phone("+461", preferred: true), Email("j@x.test", preferred: true));
        ok.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Setting_the_same_channels_is_a_noop_and_keeps_the_etag()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, email: "jane@x.test");

        var resp = await SetChannels(api, c.Id, Email("jane@x.test"));
        resp.EnsureSuccessStatusCode();
        Assert.Equal(c.Etag, (await resp.Content.ReadFromJsonAsync<ContactDto>())!.Etag);   // unchanged → no new event
    }

    [Fact]
    public async Task Channels_round_trip_the_dav_seam_with_type_and_pref()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, "Jane", "Doe");
        (await SetChannels(api, c.Id, Phone("+4670", "cell", preferred: true))).EnsureSuccessStatusCode();

        var vcf = await api.GetStringAsync($"/dav-backend/u/{Uri.EscapeDataString(Alice)}/collections/{ab}/resources/{c.ExternalId}");
        Assert.Contains("TEL;TYPE=cell,pref:+4670", vcf);

        (await PutVcfAsync(api, Alice, ab, c.ExternalId, vcf)).EnsureSuccessStatusCode();
        var dto = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!;
        var tel = Assert.Single(dto.Channels);
        Assert.Equal((ReachMedium.Phone, "+4670", "cell", true), (tel.Medium, tel.Value, tel.Type, tel.Preferred));
    }

    [Fact]
    public async Task Set_tags_prunes_where_revise_would_only_union()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        await api.PutAsJsonAsync($"/contacts/{c.Id}", new ReviseContactRequest { Tags = ["work", "friend"] });
        var unioned = await api.PutAsJsonAsync($"/contacts/{c.Id}", new ReviseContactRequest { Tags = ["family"] });   // revise unions on
        Assert.Equal(3, (await unioned.Content.ReadFromJsonAsync<ContactDto>())!.Tags!.Length);

        var pruned = await api.PutAsJsonAsync($"/contacts/{c.Id}/tags", new SetContactTagsRequest { Tags = ["family"] });
        Assert.Equal("family", Assert.Single((await pruned.Content.ReadFromJsonAsync<ContactDto>())!.Tags!));
    }

    [Fact]
    public async Task Set_channels_requires_write_access()
    {
        var alice = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(alice);
        var c = await CreateContactAsync(alice, ab);
        await alice.PostAsJsonAsync($"/address-books/{ab}/owners", new GrantOwnerRequest { Email = Bob, Access = "read" });

        var bob = Factory.ApiClient(Bob);
        var attempt = await SetChannels(bob, c.Id, Phone("+1000"));
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }
}
