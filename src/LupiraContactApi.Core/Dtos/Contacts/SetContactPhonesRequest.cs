namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's phone numbers (empty clears). Unlike <c>ReviseContact</c>,
/// which only unions, this can remove a number. Entries are trimmed and de-duplicated case-insensitively.</summary>
public sealed class SetContactPhonesRequest
{
    public required string[] Phones { get; set; }
}
