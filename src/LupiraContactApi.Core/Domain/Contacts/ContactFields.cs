using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts;

/// <summary>
/// Structured contact fields (name parts + nickname + typed reach channels). No <c>FullName</c> — the display
/// name is composed from the parts, and every serialized representation is regenerated from these fields (no raw blob is stored).
/// <c>DisplayNameFormat</c> is a rendering preference that rides here for persistence but is excluded from the content hash.
/// </summary>
public sealed record ContactFields(
    string? GivenName,
    string? MiddleName,
    string? FamilyName,
    string? Nickname,
    IReadOnlyList<ContactReachChannel>? Channels,
    PartialDate? Birthday,
    string[]? Tags,
    string? Notes = null,
    string? Pronouns = null,
    DisplayNameFormat DisplayNameFormat = DisplayNameFormat.FirstLast,
    ContactKind Kind = ContactKind.Individual);
