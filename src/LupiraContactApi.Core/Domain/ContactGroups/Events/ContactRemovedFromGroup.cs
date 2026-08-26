namespace LupiraContactApi.Core.Domain.ContactGroups.Events;

public sealed record ContactRemovedFromGroup(Guid GroupId, Guid ContactId);
