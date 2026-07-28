using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Update an existing contact by <b>merge</b>: a provided scalar overwrites, provided reach channels/tags
/// are unioned onto the existing values (deduped), and any field left null keeps its current value. Enrichment never
/// wipes what it didn't mention. Use <c>PUT /contacts/{id}/channels</c> to remove channels. The address book isn't changeable here.</summary>
public sealed class ReviseContactRequest
{
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

    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution of the core fields.
    /// Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }
}
