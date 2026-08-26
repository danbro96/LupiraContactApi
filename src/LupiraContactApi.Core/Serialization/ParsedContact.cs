using LupiraContactApi.Core.Domain;

namespace LupiraContactApi.Core.Serialization;

/// <summary>Projection parsed out of a client-PUT vCard. For <c>Profiles</c>/<c>Deceased</c>/<c>EmergencyContactIds</c>/<c>Notes</c>/<c>Pronouns</c>/<c>Kind</c>,
/// null means the property was absent from the card (most clients never emit the X-props) — the write path preserves
/// the existing value then, instead of clearing it.</summary>
public sealed record ParsedContact(
    string FullName, string? GivenName, string? FamilyName, string? Organization,
    ContactReachChannel[]? Channels, PartialDate? Birthday, ContactRelation[]? Relations,
    Guid[]? EmergencyContactIds, ContactSocialProfile[]? Profiles, bool? Deceased, DateOnly? DeathDate,
    string? Notes = null, string? Pronouns = null, ContactKind? Kind = null);
