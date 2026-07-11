using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's postal addresses; each entry needs a geo place id or a formatted address.</summary>
public sealed class SetContactAddressesRequest
{
    public required List<ContactPostalAddress> Addresses { get; set; }
}
