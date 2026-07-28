namespace LupiraContactApi.Domain;

// ContentHash is derived from the resulting aggregate state (see Contact.RecomputeHash / ContactContent) and is
// NOT carried on events. Actor + timestamp come from Marten event metadata (see EventActor), not event fields.

/// <summary>Created from structured fields.</summary>
public record ContactCreated(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Fields);

/// <summary>Created or replaced from an external sync write — parsed into structured fields (no blob retained).</summary>
public record ContactImported(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Parsed);

public record ContactRevised(Guid ContactId, ContactFields Fields);
public record ContactDeleted(Guid ContactId);
public record ContactRestored(Guid ContactId);

/// <summary>Replaces the contact's postal addresses (each an optional geo place id + formatted address).</summary>
public record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses);

/// <summary>Replaces the contact's social/IM handles.</summary>
public record ContactProfilesReplaced(Guid ContactId, IReadOnlyList<ContactSocialProfile> Profiles);

/// <summary>Upserts one directed relation edge keyed by (ToContactId, Kind); re-adding revives an ended edge.</summary>
public record ContactRelationAdded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string? Label, DateOnly? Since = null, string? Note = null);

/// <summary>The edge was a mistake and is erased. A relationship that ran its course is <see cref="ContactRelationEnded"/> instead.</summary>
public record ContactRelationRemoved(Guid ContactId, Guid ToContactId, ContactRelationKind Kind);

/// <summary>The relationship ended (divorce, falling-out) — the edge stays, flagged, with an optional end date.</summary>
public record ContactRelationEnded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, DateOnly? Until);

/// <summary>Wholesale replace from an external sync write (mirrors <see cref="ContactAddressesReplaced"/>).</summary>
public record ContactRelationsReplaced(Guid ContactId, IReadOnlyList<ContactRelation> Relations);

/// <summary>Replaces the ordered emergency-contact designation (order = priority). A designation, not a kinship.</summary>
public record ContactEmergencyContactsReplaced(Guid ContactId, IReadOnlyList<Guid> ContactIds);

/// <summary>The person died. Deceased contacts stay in the kinship graph — death is not deletion. Date may be unknown.</summary>
public record ContactMarkedDeceased(Guid ContactId, DateOnly? DeathDate);

/// <summary>Undo of <see cref="ContactMarkedDeceased"/> (recorded in error).</summary>
public record ContactDeceasedCleared(Guid ContactId);

/// <summary>Sets (or clears, when null) the avatar reference — a URL/media id, never image bytes. Outside the
/// canonical content like postal addresses, so it does not move the ETag.</summary>
public record ContactAvatarSet(Guid ContactId, string? Ref);

/// <summary>Replaces the contact's annotation metadata (the merged JSON object — merge happens in the service).
/// Outside the canonical content like the avatar, so it does not move the ETag. Carries completeness N/A acknowledgments.</summary>
public record ContactMetadataAttached(Guid ContactId, string MetadataJson);
