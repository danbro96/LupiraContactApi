using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Serialization;

/// <summary>Projection parsed out of a client-PUT vCard. For <c>Profiles</c>/<c>Deceased</c>/<c>EmergencyContactIds</c>/<c>Notes</c>/<c>Pronouns</c>/<c>Kind</c>/<c>Nickname</c>,
/// null means the property was absent from the card (most clients never emit the X-props) — the write path preserves
/// the existing value then, instead of clearing it. An empty <c>Nickname</c> was stated empty, so it clears.
/// <c>MiddleName</c> is the N field's additional names, authoritative like the other name parts.</summary>
public sealed record ParsedContact(
    string FullName, string? GivenName, string? FamilyName, string? Organization,
    ContactReachChannel[]? Channels, PartialDate? Birthday, ResolvedRelation[]? Relations,
    Guid[]? EmergencyContactIds, ContactSocialProfile[]? Profiles, bool? Deceased, DateOnly? DeathDate,
    string? Notes = null, string? Pronouns = null, ContactKind? Kind = null, string? MiddleName = null, string? Nickname = null);
