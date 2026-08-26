namespace LupiraContactApi.Core.Domain.ContactGroups;

/// <summary>One contact's membership in a group. <see cref="Role"/> is the title held in an <c>Organization</c>
/// (null for personal groupings); <see cref="Since"/>/<see cref="Until"/> bound the tenure when known.</summary>
public sealed class GroupMembership
{
    public Guid ContactId { get; set; }

    public string? Role { get; set; }

    public DateOnly? Since { get; set; }

    public DateOnly? Until { get; set; }
}
