namespace LupiraContactApi.Core.Domain.ContactGroups.Events;

public sealed record ContactGroupRenamed(Guid GroupId, string Name);
