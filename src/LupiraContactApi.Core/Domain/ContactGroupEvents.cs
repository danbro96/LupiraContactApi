namespace LupiraContactApi.Domain;

// Actor + timestamp come from Marten event metadata (see EventActor), not event fields.

public record ContactGroupCreated(Guid GroupId, Guid AddressBookId, ContactGroupKind Kind, string Name, string? ExternalId);
public record ContactGroupRenamed(Guid GroupId, string Name);

/// <summary>Upserts a membership keyed by ContactId; re-adding updates the role/dates. For an <c>Organization</c>
/// group the <c>Role</c> is the person's title there (a person can hold several jobs via several memberships).</summary>
public record ContactAddedToGroup(Guid GroupId, Guid ContactId, string? Role = null, DateOnly? Since = null, DateOnly? Until = null);
public record ContactRemovedFromGroup(Guid GroupId, Guid ContactId);
public record ContactGroupDeleted(Guid GroupId);
