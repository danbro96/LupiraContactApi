using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The profiles/addresses write paths: wholesale replace, service canonicalization + URL derivation,
/// preferred-handle validation, the addresses-don't-touch-the-version-tag rule, and DAV preserve/replace semantics.</summary>
public sealed class ContactProfilesAddressesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task<HttpResponseMessage> PutProfilesAsync(HttpClient api, Guid id, params ContactSocialProfile[] profiles) =>
        await api.PutAsJsonAsync($"/contacts/{id}/profiles", new SetContactProfilesRequest { Profiles = [.. profiles] });

    [Fact]
    public async Task Profiles_replace_wholesale_derive_urls_and_bump_the_etag()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var resp = await PutProfilesAsync(api, c.Id,
            new ContactSocialProfile { Service = " Telegram ", Handle = "@jane", Preferred = true },
            new ContactSocialProfile { Service = "messenger", Handle = "jane.doe" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;

        Assert.Equal(2, dto.Profiles.Count);
        var tg = dto.Profiles.Single(p => p.Service == "telegram");
        Assert.Equal("jane", tg.Handle);
        Assert.Equal("https://t.me/jane", tg.Url);   // derived
        Assert.True(tg.Preferred);
        Assert.Equal("https://m.me/jane.doe", dto.Profiles.Single(p => p.Service == "messenger").Url);
        Assert.NotEqual(c.Etag, dto.Etag);   // profiles are content-bearing

        var replaced = await PutProfilesAsync(api, c.Id, new ContactSocialProfile { Service = "signal", Handle = "jane.01" });
        var only = Assert.Single((await replaced.Content.ReadFromJsonAsync<ContactDto>())!.Profiles);
        Assert.Equal("signal", only.Service);
    }

    [Fact]
    public async Task Profile_validation_rejects_blank_handles_and_double_preferred()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var blank = await PutProfilesAsync(api, c.Id, new ContactSocialProfile { Service = "telegram", Handle = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var doublePref = await PutProfilesAsync(api, c.Id,
            new ContactSocialProfile { Service = "telegram", Handle = "a", Preferred = true },
            new ContactSocialProfile { Service = "telegram", Handle = "b", Preferred = true });
        Assert.Equal(HttpStatusCode.BadRequest, doublePref.StatusCode);
    }

    [Fact]
    public async Task Duplicate_service_handle_pairs_collapse_silently()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var resp = await PutProfilesAsync(api, c.Id,
            new ContactSocialProfile { Service = "telegram", Handle = "jane" },
            new ContactSocialProfile { Service = "Telegram", Handle = "@Jane" });
        resp.EnsureSuccessStatusCode();
        Assert.Single((await resp.Content.ReadFromJsonAsync<ContactDto>())!.Profiles);
    }

    [Fact]
    public async Task Addresses_replace_wholesale_without_touching_the_etag()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        var place = Guid.NewGuid();

        var resp = await api.PutAsJsonAsync($"/contacts/{c.Id}/addresses", new SetContactAddressesRequest
        {
            Addresses = [new ContactPostalAddress { PlaceId = place, Type = ContactAddressType.Home }],
        });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal(place, Assert.Single(dto.Addresses).PlaceId);
        Assert.Equal(c.Etag, dto.Etag);   // addresses are outside the canonical content

        var invalid = await api.PutAsJsonAsync($"/contacts/{c.Id}/addresses", new SetContactAddressesRequest
        {
            Addresses = [new ContactPostalAddress { Type = ContactAddressType.Home }],   // neither place nor text
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Dav_round_trip_preserves_profiles_when_absent_and_replaces_when_present()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, "Jane", "Doe");
        (await PutProfilesAsync(api, c.Id, new ContactSocialProfile { Service = "telegram", Handle = "jane" })).EnsureSuccessStatusCode();

        var vcf = await api.GetStringAsync($"/dav-backend/u/{Uri.EscapeDataString(Email)}/collections/{ab}/resources/{c.ExternalId}");
        Assert.Contains("X-SOCIALPROFILE;TYPE=telegram:https://t.me/jane", vcf);

        // A phone syncs a card with no X-SOCIALPROFILE lines — profiles survive, and the ETag covers them.
        var put = await PutVcfAsync(api, Email, ab, c.ExternalId, MinimalVcf(c.ExternalId, "Jane Doe"));
        put.EnsureSuccessStatusCode();
        var preserved = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!;
        Assert.Equal("jane", Assert.Single(preserved.Profiles).Handle);
        Assert.Equal(put.Headers.ETag!.Tag.Trim('"'), preserved.Etag);

        // A card that does carry the prop is authoritative.
        var withProfile = MinimalVcf(c.ExternalId, "Jane Doe").Replace("END:VCARD", "X-SOCIALPROFILE;TYPE=signal:jane.01\r\nEND:VCARD");
        (await PutVcfAsync(api, Email, ab, c.ExternalId, withProfile)).EnsureSuccessStatusCode();
        var replaced = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!;
        var only = Assert.Single(replaced.Profiles);
        Assert.Equal("signal", only.Service);
        Assert.Equal("jane.01", only.Handle);
    }
}
