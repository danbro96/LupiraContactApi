using System.Net.Http.Json;
using Lupira.Testing.Postgres;
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

        var resp = await Factory.ScopedClient("svc@x.test", "internal:read").PostAsJsonAsync("/internal/contacts/describe",
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
    public async Task Relation_lines_read_the_same_whichever_side_stores_them()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var child = await CreateContactAsync(api, book, "Cara", "Child");
        var parent = await CreateContactAsync(api, book, "Pat", "Parent");
        (await api.PostAsJsonAsync($"/contacts/{child.Id}/relations", new AddContactRelationRequest
        {
            ToContactId = parent.Id,
            Kind = ContactRelationKind.Parent,
            Label = "mum",
        })).EnsureSuccessStatusCode();

        var resp = await Factory.ScopedClient("svc@x.test", "internal:read").PostAsJsonAsync("/internal/contacts/describe",
            new DescribeContactsRequest { ContactIds = [child.Id, parent.Id] });
        resp.EnsureSuccessStatusCode();

        var byId = (await resp.Content.ReadFromJsonAsync<DescribeContactsResponse>())!.Contacts.ToDictionary(c => c.ContactId);
        Assert.Equal(["mum: Pat Parent"], byId[child.Id].Relations);
        Assert.Equal(["child: Cara Child"], byId[parent.Id].Relations);   // the label is the child's word, not the parent's
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
