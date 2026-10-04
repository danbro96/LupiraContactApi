using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

public sealed class CompletenessTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Thin_worklist_ranks_thinnest_first_and_respects_take()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var thin = await CreateContactAsync(api, abId, "Bare", "Minimum");
        var rich = (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = abId,
            GivenName = "Rich",
            FamilyName = "Record",
            Birthday = new PartialDate(1980, 1, 1),
            Channels =
            [
                new ContactReachChannel(ReachMedium.Email, "rich@x.test", null, false),
                new ContactReachChannel(ReachMedium.Phone, "+46123456", null, false),
            ],
        })).Content.ReadFromJsonAsync<ContactDto>())!;

        var list = await api.GetFromJsonAsync<List<ContactDto>>($"/contacts/thin?addressBookId={abId}");
        Assert.Equal([thin.Id, rich.Id], list!.Select(c => c.Id));

        var one = await api.GetFromJsonAsync<List<ContactDto>>($"/contacts/thin?addressBookId={abId}&take=1");
        Assert.Equal(thin.Id, Assert.Single(one!).Id);
    }

    [Fact]
    public async Task Acknowledging_na_via_metadata_raises_the_score()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var created = (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = abId,
            GivenName = "Nearly",
            FamilyName = "Done",
            Birthday = new PartialDate(1975, 6, 6),
            Channels =
            [
                new ContactReachChannel(ReachMedium.Email, "nearly@x.test", null, false),
                new ContactReachChannel(ReachMedium.Phone, "+46987", null, false),
            ],
        })).Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.True(created.Completeness!.Score < 1);

        var resp = await api.PostAsJsonAsync($"/contacts/{created.Id}/metadata",
            new { completeness = new { na = new[] { "postalAddress", "organisation", "relations" } } });
        resp.EnsureSuccessStatusCode();
        var updated = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;

        Assert.Equal(1, updated.Completeness!.Score);
        Assert.Empty(updated.Completeness.Gaps);
    }

    [Fact]
    public async Task Non_object_metadata_is_rejected_with_problem_details()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId);

        var resp = await api.PostAsync($"/contacts/{contact.Id}/metadata", new StringContent("[1]", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("null")]
    [InlineData("not json {")]
    public async Task Mcp_attach_metadata_rejects_non_object_json(string metadataJson)
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId);
        await using var mcp = await McpAsync(Email);

        var result = await mcp.CallToolAsync("attach_metadata",
            new Dictionary<string, object?> { ["contactId"] = contact.Id, ["metadataJson"] = metadataJson });

        Assert.True(result.IsError);
        Assert.Contains("`metadataJson` must be a JSON object.", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task Organisation_card_skips_person_asks_and_kind_round_trips()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var venue = (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = abId,
            Kind = ContactKind.Organization,
            GivenName = "Trattoria Nonna",
            Channels = [new ContactReachChannel(ReachMedium.Phone, "+4681234", null, false)],
        })).Content.ReadFromJsonAsync<ContactDto>())!;

        Assert.Equal(ContactKind.Organization, venue.Kind);
        Assert.DoesNotContain(venue.Completeness!.Gaps, g => g.Field is "birthday" or "organisation" or "secondaryReach" or "relations");
        Assert.Contains(venue.Completeness.Gaps, g => g.Field == "postalAddress");

        var got = await api.GetFromJsonAsync<ContactDto>($"/contacts/{venue.Id}");
        Assert.Equal(ContactKind.Organization, got!.Kind);
    }
}
