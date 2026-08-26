using LupiraContactApi.Core.Domain;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's postal addresses; each entry needs a geo place id.</summary>
public sealed class SetContactAddressesRequest
{
    public required List<ContactPostalAddress> Addresses { get; set; }
    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution. Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

}
