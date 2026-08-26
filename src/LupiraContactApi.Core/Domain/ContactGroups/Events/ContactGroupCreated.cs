using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.ContactGroups.Events;

public sealed record ContactGroupCreated(Guid GroupId, Guid AddressBookId, ContactGroupKind Kind, string Name, string? ExternalId);
