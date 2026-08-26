namespace LupiraContactApi.Core.Domain.ContactGroups.Events;

/// <summary>Upserts a membership keyed by ContactId; re-adding updates the role/dates. For an <c>Organization</c>
/// group the <c>Role</c> is the person's title there (a person can hold several jobs via several memberships).</summary>
public sealed record ContactAddedToGroup(Guid GroupId, Guid ContactId, string? Role = null, DateOnly? Since = null, DateOnly? Until = null);
