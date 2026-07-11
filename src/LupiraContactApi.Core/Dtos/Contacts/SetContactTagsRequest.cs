namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's tags (empty clears). Unlike <c>ReviseContact</c>,
/// which only unions, this can remove a tag. Entries are trimmed and de-duplicated case-insensitively.</summary>
public sealed class SetContactTagsRequest
{
    public required string[] Tags { get; set; }
}
