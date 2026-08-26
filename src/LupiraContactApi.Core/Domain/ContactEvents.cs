namespace LupiraContactApi.Domain;

// ContentHash is derived from the resulting aggregate state (see Contact.RecomputeHash / ContactContent) and is
// NOT carried on events. Actor + timestamp come from Marten event metadata (see EventActor), not event fields.

/// <summary>Created from structured fields.</summary>
public sealed record ContactCreated(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Fields);

/// <summary>Created or replaced from an external sync write — parsed into structured fields (no blob retained).</summary>
public sealed record ContactImported(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Parsed);

// Guarded mutating events carry an optional client stamp (OccurredAt = the client's wall clock, CommandId = its
// minted UUIDv7) — trailing + defaulted so pre-existing serialized events and non-stamping writers stay wire-
// compatible. The stamp feeds SectionLww so a stale offline replay loses instead of clobbering a newer write;
// unstamped events fall back to the event's server timestamp + sequence, preserving append order.

public sealed record ContactRevised(Guid ContactId, ContactFields Fields,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
public sealed record ContactDeleted(Guid ContactId);
public sealed record ContactRestored(Guid ContactId);

/// <summary>Replaces the contact's postal addresses (each an optional geo place id + formatted address).</summary>
public sealed record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

/// <summary>Replaces the contact's social/IM handles.</summary>
public sealed record ContactProfilesReplaced(Guid ContactId, IReadOnlyList<ContactSocialProfile> Profiles,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

/// <summary>Upserts one directed relation edge keyed by (ToContactId, Kind); re-adding revives an ended edge.</summary>
public sealed record ContactRelationAdded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string? Label, DateOnly? Since = null, string? Note = null);

/// <summary>The edge was a mistake and is erased. A relationship that ran its course is <see cref="ContactRelationEnded"/> instead.</summary>
public sealed record ContactRelationRemoved(Guid ContactId, Guid ToContactId, ContactRelationKind Kind);

/// <summary>The relationship ended (divorce, falling-out) — the edge stays, flagged, with an optional end date.</summary>
public sealed record ContactRelationEnded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, DateOnly? Until);

/// <summary>Wholesale replace from an external sync write (mirrors <see cref="ContactAddressesReplaced"/>).</summary>
public sealed record ContactRelationsReplaced(Guid ContactId, IReadOnlyList<ContactRelation> Relations);

/// <summary>Replaces the ordered emergency-contact designation (order = priority). A designation, not a kinship.</summary>
public sealed record ContactEmergencyContactsReplaced(Guid ContactId, IReadOnlyList<Guid> ContactIds);

/// <summary>The person died. Deceased contacts stay in the kinship graph — death is not deletion. Date may be unknown.</summary>
public sealed record ContactMarkedDeceased(Guid ContactId, DateOnly? DeathDate,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

/// <summary>Undo of <see cref="ContactMarkedDeceased"/> (recorded in error).</summary>
public sealed record ContactDeceasedCleared(Guid ContactId,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

/// <summary>Sets (or clears, when null) the avatar reference — a URL/media id, never image bytes. Outside the
/// canonical content like postal addresses, so it does not move the ETag.</summary>
public sealed record ContactAvatarSet(Guid ContactId, string? Ref,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

/// <summary>Replaces the contact's annotation metadata (the merged JSON object — merge happens in the service).
/// Outside the canonical content like the avatar, so it does not move the ETag. Carries completeness N/A acknowledgments.</summary>
public sealed record ContactMetadataAttached(Guid ContactId, string MetadataJson,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
