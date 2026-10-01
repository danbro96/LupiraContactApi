namespace LupiraContactApi.Core.Dtos.Contacts;

public sealed class MoveContactRequest
{
    /// <summary>The address book to move the contact into.</summary>
    public required Guid AddressBookId { get; set; }
}
