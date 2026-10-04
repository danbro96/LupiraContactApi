using JasperFx.Events.Projections;
using LupiraContactApi.Core.Domain.AddressBooks;
using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.ContactGroups.Events;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Domain.PlaceEntries;
using LupiraContactApi.Core.Domain.PlaceEntries.Events;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Relationships.Events;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Residencies.Events;
using LupiraContactApi.Core.Domain.Shared;
using Marten;
using Weasel.Core;

namespace LupiraContactApi.Core.Data;

/// <summary>Configures the single Marten store for the Contact API: event-sourced aggregates (inline snapshots)
/// + plain documents, in the <c>contact</c> schema. Enums serialize as strings. Every event has a stable, explicit
/// type name so the CLR types can be renamed freely; shape changes after go-live register an upcaster (see
/// <c>Domain/Events/Upcasters</c>) rather than mutating a record in place.</summary>
public static class MartenRegistrations
{
    public static StoreOptions UseLupiraContact(this StoreOptions opts)
    {
        opts.DatabaseSchemaName = "contact";
        opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        // Rich append: sequences + versions are reserved client-side BEFORE inline projections run, so
        // Contact.Touch can stamp UpdatedSequence (the sync feed's cursor watermark) from IEvent.Sequence.
        // Under the default Quick mode the sequence is assigned server-side at INSERT and reads 0 in Apply.
        opts.Events.AppendMode = JasperFx.Events.EventAppendMode.Rich;

        // Capture provenance on every event — actor (header) + correlation/causation. Unbackfillable, so on from day one.
        opts.Events.MetadataConfig.HeadersEnabled = true;
        opts.Events.MetadataConfig.CorrelationIdEnabled = true;
        opts.Events.MetadataConfig.CausationIdEnabled = true;

        // Stable event type names (persisted as mt_events.type), decoupled from the CLR type name.
        opts.Events.MapEventType<ContactCreated>("contact_created");
        opts.Events.MapEventType<ContactImported>("contact_imported");
        opts.Events.MapEventType<ContactRevised>("contact_revised");
        opts.Events.MapEventType<ContactDeleted>("contact_deleted");
        opts.Events.MapEventType<ContactRestored>("contact_restored");
        opts.Events.MapEventType<ContactMoved>("contact_moved");
        opts.Events.MapEventType<ContactAddressesReplaced>("contact_addresses_replaced");
        opts.Events.MapEventType<ContactProfilesReplaced>("contact_profiles_replaced");
        opts.Events.MapEventType<ContactRelationAdded>("contact_relation_added");
        opts.Events.MapEventType<ContactRelationRemoved>("contact_relation_removed");
        opts.Events.MapEventType<ContactRelationEnded>("contact_relation_ended");
        opts.Events.MapEventType<ContactRelationsReplaced>("contact_relations_replaced");
        opts.Events.MapEventType<ContactEmergencyContactsReplaced>("contact_emergency_contacts_replaced");
        opts.Events.MapEventType<ContactMarkedDeceased>("contact_marked_deceased");
        opts.Events.MapEventType<ContactDeceasedCleared>("contact_deceased_cleared");
        opts.Events.MapEventType<ContactAvatarSet>("contact_avatar_set");
        opts.Events.MapEventType<ContactMetadataAttached>("contact_metadata_attached");
        opts.Events.MapEventType<ContactGroupCreated>("contact_group_created");
        opts.Events.MapEventType<ContactGroupRenamed>("contact_group_renamed");
        opts.Events.MapEventType<ContactGroupMoved>("contact_group_moved");
        opts.Events.MapEventType<ContactAddedToGroup>("contact_added_to_group");
        opts.Events.MapEventType<ContactRemovedFromGroup>("contact_removed_from_group");
        opts.Events.MapEventType<ContactGroupDeleted>("contact_group_deleted");
        opts.Events.MapEventType<RelationshipEstablished>("relationship_established");
        opts.Events.MapEventType<RelationshipRevised>("relationship_revised");
        opts.Events.MapEventType<RelationshipLabelled>("relationship_labelled");
        opts.Events.MapEventType<RelationshipEnded>("relationship_ended");
        opts.Events.MapEventType<RelationshipRevived>("relationship_revived");
        opts.Events.MapEventType<RelationshipRemoved>("relationship_removed");
        opts.Events.MapEventType<ResidencyStarted>("residency_started");
        opts.Events.MapEventType<ResidencyRevised>("residency_revised");
        opts.Events.MapEventType<ResidencyMovedOut>("residency_moved_out");
        opts.Events.MapEventType<ResidencyRemoved>("residency_removed");
        opts.Events.MapEventType<EntryCodeSet>("entry_code_set");
        opts.Events.MapEventType<EntryCodeRemoved>("entry_code_removed");

        // Event-sourced aggregates (resource read models) — inline for read-your-write.
        opts.Projections.Snapshot<Contact>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<ContactGroup>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Relationship>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Residency>(SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<PlaceEntry>(SnapshotLifecycle.Inline);
        // The sync changes feed pages contacts by "touched since cursor" — indexed so the delta query never scans.
        opts.Schema.For<Contact>().Index(x => x.UpdatedSequence);
        // A contact's relationships are found from either end; the relationships feed pages by watermark like contacts.
        opts.Schema.For<Relationship>().Index(x => x.Low).Index(x => x.High).Index(x => x.UpdatedSequence);
        // Residents of a place and a contact's residencies are both looked up; the feeds page by watermark.
        opts.Schema.For<Residency>().Index(x => x.ContactId).Index(x => x.PlaceId).Index(x => x.UpdatedSequence);
        opts.Schema.For<PlaceEntry>().Index(x => x.PlaceId).Index(x => x.UpdatedSequence);

        // Idempotency ledger (Idempotency-Key on mutations); identity = the client's command id, so a duplicate
        // key is a PK violation that rolls back the whole transaction (see Data/Idempotency).
        opts.Schema.For<ProcessedCommand>().Identity(x => x.CommandId);

        // Plain documents (collections, identity) + the indexes the services query by.
        // Unique sub: without it, concurrent first-sight logins fork one login into two principals.
        // Email stays non-unique — mutable, and a placeholder row shares it until the sub upgrade lands.
        opts.Schema.For<Principal>().Index(x => x.AuthentikSub, i => i.IsUnique = true).Index(x => x.Email);
        opts.Schema.For<AddressBook>();
        opts.Schema.For<AddressBookOwner>().Index(x => x.PrincipalId).Index(x => x.AddressBookId);

        return opts;
    }
}
