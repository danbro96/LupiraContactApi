using LupiraContactApi.Core.Domain;
using LupiraContactApi.Core.Dtos.Contacts;
using System.Net.Http.Json;
using System.Net;
using System.Text;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The profiles/addresses write paths: wholesale replace, service canonicalization + URL derivation,
/// preferred-handle validation, the addresses-don't-touch-the-version-tag rule, and DAV preserve/replace semantics.</summary>
public sealed class ContactProfilesAddressesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task<HttpResponseMessage> PutProfilesAsync(HttpClient api, Guid id, params ContactSocialProfileInput[] profiles) =>
        await api.PutAsJsonAsync($"/contacts/{id}/profiles", new SetContactProfilesRequest { Profiles = [.. profiles] });

    [Fact]
    public async Task Profiles_replace_wholesale_derive_urls_and_bump_the_etag()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var resp = await PutProfilesAsync(api, c.Id,
            new ContactSocialProfileInput { Service = " Telegram ", Handle = "@jane", Preferred = true },
            new ContactSocialProfileInput { Service = "messenger", Handle = "jane.doe" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;

        Assert.Equal(2, dto.Profiles.Count);
        var tg = dto.Profiles.Single(p => p.Service == "telegram");
        Assert.Equal("jane", tg.Handle);
        Assert.Equal("https://t.me/jane", tg.Url);   // derived
        Assert.True(tg.Preferred);
        Assert.Equal("https://m.me/jane.doe", dto.Profiles.Single(p => p.Service == "messenger").Url);
        Assert.NotEqual(c.Etag, dto.Etag);   // profiles are content-bearing

        var replaced = await PutProfilesAsync(api, c.Id, new ContactSocialProfileInput { Service = "signal", Handle = "jane.01" });
        var only = Assert.Single((await replaced.Content.ReadFromJsonAsync<ContactDto>())!.Profiles);
        Assert.Equal("signal", only.Service);
    }

    [Fact]
    public async Task Profile_validation_rejects_blank_handles_and_double_preferred()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var blank = await PutProfilesAsync(api, c.Id, new ContactSocialProfileInput { Service = "telegram", Handle = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var doublePref = await PutProfilesAsync(api, c.Id,
            new ContactSocialProfileInput { Service = "telegram", Handle = "a", Preferred = true },
            new ContactSocialProfileInput { Service = "telegram", Handle = "b", Preferred = true });
        Assert.Equal(HttpStatusCode.BadRequest, doublePref.StatusCode);
    }

    [Fact]
    public async Task Duplicate_service_handle_pairs_collapse_silently()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var resp = await PutProfilesAsync(api, c.Id,
            new ContactSocialProfileInput { Service = "telegram", Handle = "jane" },
            new ContactSocialProfileInput { Service = "Telegram", Handle = "@Jane" });
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

        // No place id -> rejected at binding (PlaceId is required on the wire).
        var invalid = await api.PutAsync($"/contacts/{c.Id}/addresses",
            new StringContent("""{"addresses":[{"type":"Home"}]}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var empty = await api.PutAsJsonAsync($"/contacts/{c.Id}/addresses", new SetContactAddressesRequest
        {
            Addresses = [new ContactPostalAddress { PlaceId = Guid.Empty, Type = ContactAddressType.Home }],
        });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Addresses_roundtrip_residency_periods_and_keep_the_etag()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        var place = Guid.NewGuid();

        var resp = await api.PutAsJsonAsync($"/contacts/{c.Id}/addresses", new SetContactAddressesRequest
        {
            Addresses = [new ContactPostalAddress
            {
                PlaceId = place, Type = ContactAddressType.Home,
                MovedIn = new FuzzyDate(2010), MovedOut = new FuzzyDate(2015, 6),
            }],
        });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
        var addr = Assert.Single(dto.Addresses);
        Assert.Equal(new FuzzyDate(2010), addr.MovedIn);
        Assert.Equal(new FuzzyDate(2015, 6), addr.MovedOut);
        Assert.Equal(c.Etag, dto.Etag);   // residency history stays outside the canonical content

        // A date-only edit is a real change, not a swallowed no-op.
        var edited = await api.PutAsJsonAsync($"/contacts/{c.Id}/addresses", new SetContactAddressesRequest
        {
            Addresses = [new ContactPostalAddress
            {
                PlaceId = place, Type = ContactAddressType.Home,
                MovedIn = new FuzzyDate(2010), MovedOut = new FuzzyDate(2016),
            }],
        });
        edited.EnsureSuccessStatusCode();
        Assert.Equal(new FuzzyDate(2016), Assert.Single((await edited.Content.ReadFromJsonAsync<ContactDto>())!.Addresses).MovedOut);
    }

    [Fact]
    public async Task Rejects_invalid_residency_dates()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        var place = Guid.NewGuid();

        async Task<HttpStatusCode> Put(FuzzyDate? movedIn, FuzzyDate? movedOut)
        {
            var r = await api.PutAsJsonAsync($"/contacts/{c.Id}/addresses", new SetContactAddressesRequest
            {
                Addresses = [new ContactPostalAddress { PlaceId = place, Type = ContactAddressType.Home, MovedIn = movedIn, MovedOut = movedOut }],
            });
            return r.StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest, await Put(new FuzzyDate(2015, 13), null));         // month 13
        Assert.Equal(HttpStatusCode.BadRequest, await Put(new FuzzyDate(2015, null, 12), null));   // day without month
        Assert.Equal(HttpStatusCode.BadRequest, await Put(new FuzzyDate(2016), new FuzzyDate(2015))); // certainly inverted
        Assert.Equal(HttpStatusCode.OK, await Put(new FuzzyDate(2015), new FuzzyDate(2015)));      // same year: compatible
    }

    [Fact]
    public async Task Dav_round_trip_preserves_profiles_when_absent_and_replaces_when_present()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, "Jane", "Doe");
        (await PutProfilesAsync(api, c.Id, new ContactSocialProfileInput { Service = "telegram", Handle = "jane" })).EnsureSuccessStatusCode();

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
