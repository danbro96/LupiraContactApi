using LupiraContactApi.Domain;

namespace LupiraContactApi.Serialization;

/// <summary>Projection parsed out of a client-PUT vCard. For <c>Profiles</c>/<c>Deceased</c>/<c>EmergencyContactIds</c>,
/// null means the property was absent from the card (most clients never emit the X-props) — the write path preserves
/// the existing value then, instead of clearing it.</summary>
public sealed record ParsedContact(
    string FullName, string? GivenName, string? FamilyName, string? Organization,
    string[]? Emails, string[]? Phones, DateOnly? Birthday, ContactRelation[]? Relations,
    Guid[]? EmergencyContactIds, ContactSocialProfile[]? Profiles, bool? Deceased, DateOnly? DeathDate);
