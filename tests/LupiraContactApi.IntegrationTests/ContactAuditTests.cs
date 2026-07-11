using JasperFx.Events;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using Marten;
using System.Net.Http.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Event provenance: mutations stamp the acting principal as the <c>actor</c> header and surface it as
/// created/updated attribution on the read model.</summary>
public sealed class ContactAuditTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Create_and_revise_attribute_the_acting_principal()
    {
        var api = Factory.ApiClient(Email);
        var me = await GetMyIdAsync(api);
        var ab = await CreateAddressBookAsync(api);

        var created = await CreateContactAsync(api, ab, "Jane", "Doe");
        Assert.Equal(me.ToString(), created.CreatedBy);
        Assert.Equal(me.ToString(), created.UpdatedBy);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        var revised = await api.PutAsJsonAsync($"/contacts/{created.Id}", new ReviseContactRequest { Nickname = "JD" });
        revised.EnsureSuccessStatusCode();
        var dto = (await revised.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal(me.ToString(), dto.CreatedBy);   // unchanged
        Assert.Equal(me.ToString(), dto.UpdatedBy);
        Assert.True(dto.UpdatedAt >= dto.CreatedAt);
    }

    [Fact]
    public async Task Events_carry_the_actor_header()
    {
        var api = Factory.ApiClient(Email);
        var me = await GetMyIdAsync(api);
        var ab = await CreateAddressBookAsync(api);
        var created = await CreateContactAsync(api, ab, "Jane", "Doe");

        await using var session = Factory.Store.QuerySession();
        var events = await session.Events.FetchStreamAsync(created.Id);
        var createEvent = Assert.Single(events, e => e.EventType == typeof(ContactCreated));
        Assert.Equal(me.ToString(), EventActor.Of(createEvent));
    }
}
