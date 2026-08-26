using LupiraContactApi.Core.Dtos.Contacts;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

public sealed class ContactsBatchTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    // The host serializes enums as strings (ConfigureHttpJsonOptions + JsonStringEnumConverter).
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Create_batch_returns_contacts_in_input_order()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);

        var resp = await api.PostAsJsonAsync("/contacts/batch", new CreateContactsBatchRequest
        {
            Contacts =
            [
                new CreateContactRequest { AddressBookId = abId, GivenName = "Bbb", FamilyName = "One" },
                new CreateContactRequest { AddressBookId = abId, GivenName = "Aaa", FamilyName = "Two" },
                new CreateContactRequest { AddressBookId = abId, GivenName = "Ccc", FamilyName = "Three" },
            ],
        });
        resp.EnsureSuccessStatusCode();

        var created = (await resp.Content.ReadFromJsonAsync<List<ContactDto>>())!;
        Assert.Equal(3, created.Count);
        // Preserves request order, NOT sorted by name.
        Assert.Equal("Bbb One", created[0].DisplayName);
        Assert.Equal("Aaa Two", created[1].DisplayName);
        Assert.Equal("Ccc Three", created[2].DisplayName);
        Assert.All(created, c => Assert.NotEqual(Guid.Empty, c.Id));
    }

    [Fact]
    public async Task Create_batch_caps_at_100()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var resp = await api.PostAsJsonAsync("/contacts/batch", new CreateContactsBatchRequest
        {
            Contacts = [.. Enumerable.Range(0, 101).Select(i => new CreateContactRequest { AddressBookId = abId, GivenName = $"N{i}", FamilyName = "X" })],
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Resolve_names_matches_ambiguous_and_notfound()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var jane = await CreateContactAsync(api, abId, "Jane", "Doe");
        await CreateContactAsync(api, abId, "Jane", "Smith");
        await CreateContactAsync(api, abId, "Anna", "Andersson");
        await CreateContactAsync(api, abId, "Anna", "Andersson"); // duplicate display name

        var resp = await api.PostAsJsonAsync("/contacts/resolve-names", new ResolveContactsByNameRequest
        {
            Names = ["Jane Doe", "Jane", "Anna Andersson", "Nonexistent Person"],
            AddressBookId = abId,
        });
        resp.EnsureSuccessStatusCode();
        var r = (await resp.Content.ReadFromJsonAsync<List<ContactNameMatch>>(Json))!;
        Assert.Equal(4, r.Count);

        // Exact single → Matched.
        Assert.Equal(NameMatchOutcome.Matched, r[0].Outcome);
        Assert.Equal(jane.Id, r[0].ContactId);

        // "Jane" substring hits two, no exact display-name equality → Ambiguous.
        Assert.Equal(NameMatchOutcome.Ambiguous, r[1].Outcome);
        Assert.Null(r[1].ContactId);
        Assert.Equal(2, r[1].Candidates.Count);

        // Two contacts share the exact display name → Ambiguous.
        Assert.Equal(NameMatchOutcome.Ambiguous, r[2].Outcome);
        Assert.Equal(2, r[2].Candidates.Count);

        // No hit at all → NotFound.
        Assert.Equal(NameMatchOutcome.NotFound, r[3].Outcome);
        Assert.Empty(r[3].Candidates);
    }

    [Fact]
    public async Task Batch_endpoints_require_authentication()
    {
        var anon = Factory.AnonymousClient();
        var create = await anon.PostAsJsonAsync("/contacts/batch",
            new CreateContactsBatchRequest { Contacts = [new CreateContactRequest { AddressBookId = Guid.NewGuid(), GivenName = "X" }] });
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);

        var resolve = await anon.PostAsJsonAsync("/contacts/resolve-names",
            new ResolveContactsByNameRequest { Names = ["X"] });
        Assert.Equal(HttpStatusCode.Unauthorized, resolve.StatusCode);
    }
}
