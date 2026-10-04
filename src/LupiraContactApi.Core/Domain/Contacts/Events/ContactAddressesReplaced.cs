namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Replaced a contact's addresses wholesale. Legacy: residencies are their own aggregate now
/// (<see cref="Residencies.Residency"/>); kept so old contact streams still deserialize, and read only by the residencies migration.</summary>
public sealed record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
