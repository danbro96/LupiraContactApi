using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Internal;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The service-to-service describe seam (comms' contact directory): descriptor material —
/// nickname, pronouns, tags, notes, rendered live relation lines — with unknown/deleted ids absent.</summary>
public sealed class InternalDescribeTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    [Fact]
    public async Task Describes_live_contacts_with_rendered_relations()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);

        var anton = (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = book,
            GivenName = "Anton",
            FamilyName = "Alfonsson",
            Nickname = "Antis",
            Pronouns = "he/him",
            Tags = ["partner", "klättring"],
            Notes = "Bor på Södermalm.",
        })).Content.ReadFromJsonAsync<ContactDto>())!;
        var mona = await CreateContactAsync(api, book, "Mona", "Broström");
        (await api.PostAsJsonAsync($"/contacts/{anton.Id}/relations", new AddContactRelationRequest
        {
            ToContactId = mona.Id,
            Kind = ContactRelationKind.Friend,
        })).EnsureSuccessStatusCode();

        var resp = await Factory.ServiceClient().PostAsJsonAsync("/internal/contacts/describe",
            new DescribeContactsRequest { ContactIds = [anton.Id, Guid.NewGuid()] });
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<DescribeContactsResponse>();
        var only = Assert.Single(result!.Contacts);
        Assert.Equal("Anton Alfonsson", only.DisplayName);
        Assert.Equal("Antis", only.Nickname);
        Assert.Equal("he/him", only.Pronouns);
        Assert.Equal(["partner", "klättring"], only.Tags);
        Assert.Equal("Bor på Södermalm.", only.Notes);
        Assert.Equal(["friend: Mona Broström"], only.Relations);
    }

    [Fact]
    public async Task Requires_the_internal_scope()
    {
        var body = new DescribeContactsRequest { ContactIds = [Guid.NewGuid()] };
        Assert.Equal(
            System.Net.HttpStatusCode.Unauthorized,
            (await Factory.AnonymousClient().PostAsJsonAsync("/internal/contacts/describe", body)).StatusCode);
        Assert.Equal(
            System.Net.HttpStatusCode.Forbidden,
            (await Factory.ApiClient(Email).PostAsJsonAsync("/internal/contacts/describe", body)).StatusCode);
    }
}
