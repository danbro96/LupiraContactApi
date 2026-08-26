namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's tags (empty clears). Unlike <c>ReviseContact</c>,
/// which only unions, this can remove a tag. Entries are trimmed and de-duplicated case-insensitively.</summary>
public sealed class SetContactTagsRequest
{
    public required string[] Tags { get; set; }
    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution. Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

}
