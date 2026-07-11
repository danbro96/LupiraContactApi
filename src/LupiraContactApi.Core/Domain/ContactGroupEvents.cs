namespace LupiraContactApi.Domain;

// Actor + timestamp come from Marten event metadata (see EventActor), not event fields.

public record ContactGroupCreated(Guid GroupId, Guid AddressBookId, ContactGroupKind Kind, string Name, string? ExternalId);
public record ContactGroupRenamed(Guid GroupId, string Name);
public record ContactAddedToGroup(Guid GroupId, Guid ContactId);
public record ContactRemovedFromGroup(Guid GroupId, Guid ContactId);
public record ContactGroupDeleted(Guid GroupId);
