namespace LupiraContactApi.Core.Domain.ContactGroups.Events;

/// <summary>The group now lives in another address book. Members stay: membership is by contact id, whichever book the contact lives in.</summary>
public sealed record ContactGroupMoved(Guid GroupId, Guid AddressBookId);
