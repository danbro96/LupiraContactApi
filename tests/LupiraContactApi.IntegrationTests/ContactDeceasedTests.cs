using LupiraContactApi.Dtos.Contacts;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Deceased marking: idempotent set/clear with version-tag semantics, the X-DEATHDATE round-trip at the DAV
/// seam, preserve-if-absent on plain client PUTs, and the remembrance-only completeness rubric.</summary>
public sealed class ContactDeceasedTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task<ContactDto> MarkDeceasedAsync(HttpClient api, Guid id, DateOnly? deathDate = null)
    {
        var resp = await api.PutAsJsonAsync($"/contacts/{id}/deceased", new SetDeceasedRequest { DeathDate = deathDate });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    [Fact]
    public async Task Mark_and_clear_change_the_etag_and_reads_reflect_the_state()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, "Gone", "Grandpa");

        var marked = await MarkDeceasedAsync(api, c.Id, new DateOnly(2020, 3, 14));
        Assert.True(marked.Deceased);
        Assert.Equal(new DateOnly(2020, 3, 14), marked.DeathDate);
        Assert.NotEqual(c.Etag, marked.Etag);

        var again = await MarkDeceasedAsync(api, c.Id, new DateOnly(2020, 3, 14));
        Assert.Equal(marked.Etag, again.Etag);   // idempotent: no event, no version churn

        var cleared = await api.DeleteAsync($"/contacts/{c.Id}/deceased");
        cleared.EnsureSuccessStatusCode();
        var dto = (await cleared.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.False(dto.Deceased);
        Assert.Null(dto.DeathDate);
        Assert.NotEqual(marked.Etag, dto.Etag);
    }

    [Fact]
    public async Task Deceased_without_a_date_is_valid()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var marked = await MarkDeceasedAsync(api, c.Id);
        Assert.True(marked.Deceased);
        Assert.Null(marked.DeathDate);
    }

    [Fact]
    public async Task Dav_get_emits_the_deceased_props()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        await MarkDeceasedAsync(api, c.Id, new DateOnly(2020, 3, 14));

        var vcf = await api.GetStringAsync($"/dav-backend/u/{Uri.EscapeDataString(Email)}/collections/{ab}/resources/{c.ExternalId}");
        Assert.Contains("X-DEATHDATE:20200314", vcf);
    }

    [Fact]
    public async Task Plain_dav_put_preserves_deceased_and_the_etag_matches_a_subsequent_get()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, "Gone", "Grandpa");
        await MarkDeceasedAsync(api, c.Id, new DateOnly(2020, 3, 14));

        // A phone syncs back a card without any X-props — deceased must survive.
        var put = await PutVcfAsync(api, Email, ab, c.ExternalId, MinimalVcf(c.ExternalId, "Gone Grandpa", "g@x.test"));
        put.EnsureSuccessStatusCode();
        var putEtag = put.Headers.ETag!.Tag.Trim('"');

        var dto = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!;
        Assert.True(dto.Deceased);
        Assert.Equal(new DateOnly(2020, 3, 14), dto.DeathDate);
        Assert.Equal(putEtag, dto.Etag);   // the hash covered the preserved state

        using var get = await api.GetAsync($"/dav-backend/u/{Uri.EscapeDataString(Email)}/collections/{ab}/resources/{c.ExternalId}");
        Assert.Equal(putEtag, get.Headers.ETag!.Tag.Trim('"'));
    }

    [Fact]
    public async Task Dav_put_with_a_deathdate_marks_the_contact_deceased()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var vcf = MinimalVcf(c.ExternalId, "Jane Doe").Replace("END:VCARD", "X-DEATHDATE:20210101\r\nEND:VCARD");
        (await PutVcfAsync(api, Email, ab, c.ExternalId, vcf)).EnsureSuccessStatusCode();

        var dto = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}"))!;
        Assert.True(dto.Deceased);
        Assert.Equal(new DateOnly(2021, 1, 1), dto.DeathDate);
    }

    [Fact]
    public async Task Completeness_stops_asking_for_reach_once_deceased()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);
        Assert.Contains(c.Completeness!.Gaps, g => g.Field == "primaryReach");

        var marked = await MarkDeceasedAsync(api, c.Id);
        Assert.DoesNotContain(marked.Completeness!.Gaps, g => g.Field is "primaryReach" or "secondaryReach" or "postalAddress" or "organisation");
        Assert.Contains(marked.Completeness!.Gaps, g => g.Field == "deathDate");
    }
}
