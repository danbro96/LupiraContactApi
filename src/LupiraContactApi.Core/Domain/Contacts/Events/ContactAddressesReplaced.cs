namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Replaces the contact's postal addresses (each an optional geo place id + formatted address).</summary>
public sealed record ContactAddressesReplaced(Guid ContactId, IReadOnlyList<ContactPostalAddress> Addresses,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
