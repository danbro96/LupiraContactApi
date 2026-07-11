namespace LupiraContactApi.Domain;

/// <summary>
/// Structured contact fields (name parts + nickname + typed reach channels). No <c>FullName</c> — the display
/// name is composed from the parts, and every serialized representation is regenerated from these fields (no raw blob is stored).
/// </summary>
public sealed record ContactFields(
    string? NamePrefix,
    string? GivenName,
    string? MiddleName,
    string? FamilyName,
    string? NameSuffix,
    string? Nickname,
    IReadOnlyList<ContactReachChannel>? Channels,
    PartialDate? Birthday,
    string[]? Tags,
    string? Notes = null,
    string? Pronouns = null);
