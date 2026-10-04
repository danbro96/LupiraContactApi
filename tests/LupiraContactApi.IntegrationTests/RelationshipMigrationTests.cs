using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Upgrades;
using Marten;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The one-shot move of legacy per-contact relation copies onto relationship streams, run against real events.</summary>
public sealed class RelationshipMigrationTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Legacy_copies_become_one_relationship_and_a_rerun_is_a_noop()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var child = await CreateContactAsync(api, ab, "Cara", "Child");
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");
        await using (var session = Factory.Store.LightweightSession())
        {
            session.Events.Append(child.Id, new ContactRelationAdded(child.Id, parent.Id, ContactRelationKind.Parent, "dad", new DateOnly(1990, 1, 1)));
            session.Events.Append(parent.Id, new ContactRelationAdded(parent.Id, child.Id, ContactRelationKind.Child, "son"));
            await session.SaveChangesAsync();
        }

        var migration = new RelationshipMigration(Factory.Store);
        Assert.Equal(1, await migration.RunAsync());
        Assert.Equal(0, await migration.RunAsync());

        var fromChild = Assert.Single((await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{child.Id}/relations"))!);
        Assert.Equal((parent.Id, ContactRelationKind.Parent, "dad", new DateOnly(1990, 1, 1)), (fromChild.ContactId, fromChild.Kind, fromChild.Label, fromChild.Since));
        var fromParent = Assert.Single((await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{parent.Id}/relations"))!);
        Assert.Equal((child.Id, ContactRelationKind.Child, "son"), (fromParent.ContactId, fromParent.Kind, fromParent.Label));
    }

    [Fact]
    public async Task Contacts_rebuild_over_legacy_events()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, ab, "Ann", "A");
        await using (var session = Factory.Store.LightweightSession())
        {
            session.Events.Append(a.Id, new ContactRelationAdded(a.Id, Guid.NewGuid(), ContactRelationKind.Friend, null));
            await session.SaveChangesAsync();
        }

        using var daemon = await Factory.Store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Contact>(CancellationToken.None);

        Assert.Equal(a.Etag, (await api.GetFromJsonAsync<ContactDto>($"/contacts/{a.Id}"))!.Etag);   // a legacy copy no longer moves the contact
    }
}
