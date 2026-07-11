namespace LupiraContactApi.Dtos.AddressBooks;

/// <summary>Update an address book by merge: a provided slug or display name overwrites, null keeps the current value.</summary>
public sealed class UpdateAddressBookRequest
{
    public string? Slug { get; set; }
    public string? DisplayName { get; set; }
}
