namespace LupiraContactApi.Dtos.AddressBooks;

/// <summary>Create an address book; the caller becomes its owner.</summary>
public sealed class CreateAddressBookRequest
{
    public required string Slug { get; set; }
    public string? DisplayName { get; set; }
}
