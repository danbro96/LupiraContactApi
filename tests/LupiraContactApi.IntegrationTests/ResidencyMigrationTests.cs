using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Upgrades;
using Marten;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The one-shot move of legacy per-contact address lists onto residency streams, run against real events.</summary>
public sealed class ResidencyMigrationTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Legacy_addresses_become_residencies_and_a_rerun_is_a_noop()
    {
        var api = Factory.ApiClient(Email);
        var c = await CreateContactAsync(api, await CreateAddressBookAsync(api));
        var (home, former) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var session = Factory.Store.LightweightSession())
        {
            session.Events.Append(c.Id, new ContactAddressesReplaced(c.Id,
            [
                new ContactPostalAddress { PlaceId = home, Type = ContactAddressType.Home },
                new ContactPostalAddress { PlaceId = former, Type = ContactAddressType.Home, MovedIn = new FuzzyDate(2010), MovedOut = new FuzzyDate(2015, 6) },
            ]));
            await session.SaveChangesAsync();
        }

        var migration = new ResidencyMigration(Factory.Store);
        Assert.Equal(2, await migration.RunAsync());
        Assert.Equal(0, await migration.RunAsync());

        var listed = (await api.GetFromJsonAsync<List<ResidencyDto>>($"/contacts/{c.Id}/residencies"))!;
        Assert.Equal(home, listed[0].PlaceId);
        Assert.Equal((new FuzzyDate(2010), new FuzzyDate(2015, 6)), (listed[1].MovedIn, listed[1].MovedOut));

        using var daemon = await Factory.Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Contact>(CancellationToken.None);
    }
}
