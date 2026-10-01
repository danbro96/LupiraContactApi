namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>The contact now lives in another address book. Id, content and ETag are unchanged.</summary>
public sealed record ContactMoved(Guid ContactId, Guid AddressBookId);
