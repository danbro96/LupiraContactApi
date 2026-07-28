using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Create a contact via REST/MCP. No <c>FullName</c> — the display name is composed from the structured parts.
/// An employer is set separately as membership in an <c>organization</c>-kind contact group.</summary>
public sealed class CreateContactRequest
{
    public required Guid AddressBookId { get; set; }
    /// <summary>Individual (default) or Organization — a business/venue card that skips person-only enrichment.</summary>
    public ContactKind? Kind { get; set; }
    public string? GivenName { get; set; }
    public string? MiddleName { get; set; }
    public string? FamilyName { get; set; }
    public string? Nickname { get; set; }
    public DisplayNameFormat? DisplayNameFormat { get; set; }
    public List<ContactReachChannel>? Channels { get; set; }
    public PartialDate? Birthday { get; set; }
    public string[]? Tags { get; set; }
    public string? Notes { get; set; }
    public string? Pronouns { get; set; }
}
