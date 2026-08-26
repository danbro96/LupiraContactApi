namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Created or replaced from an external sync write — parsed into structured fields (no blob retained).</summary>
public sealed record ContactImported(Guid ContactId, Guid AddressBookId, string ExternalId, ContactFields Parsed);
