namespace LupiraContactApi.Domain;

/// <summary>Created from structured fields; hash covers the canonical content (see <see cref="ContactContent"/>).</summary>
public record ContactCreated(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Fields, string ContentHash);

/// <summary>Created or replaced from an external sync write — parsed into structured fields (no blob retained).</summary>
public record ContactImported(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Parsed, string ContentHash);

public record ContactRevised(Guid ContactId, ContactFields Fields, string ContentHash);
public record ContactDeleted(Guid ContactId, DateTimeOffset At);
public record ContactRestored(Guid ContactId, string ContentHash);

/// <summary>Replaces the contact's postal addresses (each an optional geo place id + formatted address).
/// Addresses are outside the canonical content, so no hash — the version tag is unchanged by design.</summary>
public record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses);

/// <summary>Replaces the contact's social/IM handles. Profiles are content-bearing, so the event carries the new hash.</summary>
public record ContactProfilesReplaced(Guid ContactId, IReadOnlyList<ContactSocialProfile> Profiles, string ContentHash);

/// <summary>Upserts one directed relation edge keyed by (ToContactId, Kind); re-adding revives an ended edge.</summary>
public record ContactRelationAdded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string? Label, string ContentHash);

/// <summary>The edge was a mistake and is erased. A relationship that ran its course is <see cref="ContactRelationEnded"/> instead.</summary>
public record ContactRelationRemoved(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string ContentHash);

/// <summary>The relationship ended (divorce, falling-out) — the edge stays, flagged, with an optional end date.</summary>
public record ContactRelationEnded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, DateOnly? Until, string ContentHash);

/// <summary>Wholesale replace from an external sync write (mirrors <see cref="ContactAddressesReplaced"/>). No hash: only ever
/// appended alongside a <see cref="ContactImported"/> whose hash already covers the final state.</summary>
public record ContactRelationsReplaced(Guid ContactId, IReadOnlyList<ContactRelation> Relations);

/// <summary>Replaces the ordered emergency-contact designation (order = priority). A designation, not a kinship.</summary>
public record ContactEmergencyContactsReplaced(Guid ContactId, IReadOnlyList<Guid> ContactIds, string ContentHash);

/// <summary>The person died. Deceased contacts stay in the kinship graph — death is not deletion. Date may be unknown.</summary>
public record ContactMarkedDeceased(Guid ContactId, DateOnly? DeathDate, string ContentHash);

/// <summary>Undo of <see cref="ContactMarkedDeceased"/> (recorded in error).</summary>
public record ContactDeceasedCleared(Guid ContactId, string ContentHash);
