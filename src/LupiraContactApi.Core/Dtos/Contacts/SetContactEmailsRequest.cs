namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's email addresses (empty clears). Unlike <c>ReviseContact</c>,
/// which only unions, this can remove an address. Entries are trimmed and de-duplicated case-insensitively.</summary>
public sealed class SetContactEmailsRequest
{
    public required string[] Emails { get; set; }
}
