namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Create many contacts in one transaction (each carries its own <c>AddressBookId</c>). Returned contacts
/// align index-for-index with <c>Contacts</c>. For bulk imports instead of many single <c>create_contact</c> calls.</summary>
public sealed class CreateContactsBatchRequest
{
    public required List<CreateContactRequest> Contacts { get; set; }
}
