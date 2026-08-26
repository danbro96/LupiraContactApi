namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>A contact's membership in a group. <c>Role</c> is the title held in an organization (null for personal
/// groupings); <c>Since</c>/<c>Until</c> bound the tenure when known.</summary>
public sealed class GroupMemberDto
{
    public required Guid ContactId { get; set; }

    public string? Role { get; set; }

    public DateOnly? Since { get; set; }

    public DateOnly? Until { get; set; }
}
