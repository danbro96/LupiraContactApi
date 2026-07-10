using JasperFx.Events.Projections;
using Marten;
using Weasel.Core;

namespace LupiraContactApi.Domain;

/// <summary>Configures the single Marten store for the Contact API: event-sourced aggregates (inline snapshots)
/// + plain documents, in the <c>contact</c> schema. Enums serialize as strings.</summary>
public static class MartenRegistrations
{
    public static StoreOptions UseLupiraContact(this StoreOptions opts)
    {
        opts.DatabaseSchemaName = "contact";
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // Event-sourced aggregates (resource read models) — inline for read-your-write.
        opts.Projections.Snapshot<Contact>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<ContactGroup>(SnapshotLifecycle.Inline);

        // Plain documents (collections, identity) + the indexes the services query by.
        opts.Schema.For<Principal>().Index(x => x.AuthentikSub).Index(x => x.Email);
        opts.Schema.For<AddressBook>();
        opts.Schema.For<AddressBookOwner>().Index(x => x.PrincipalId).Index(x => x.AddressBookId);

        return opts;
    }
}
