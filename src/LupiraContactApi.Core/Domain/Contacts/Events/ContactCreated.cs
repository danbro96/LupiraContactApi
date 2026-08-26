namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Created from structured fields.</summary>
public sealed record ContactCreated(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Fields);
