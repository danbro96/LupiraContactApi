namespace LupiraContactApi.Core.Dtos.Contacts;

public sealed class MoveContactGroupResponse
{
    public required ContactGroupDto Group { get; set; }

    /// <summary>Members moved with the group (always 0 without <c>includeMembers</c>).</summary>
    public required int MembersMoved { get; set; }

    /// <summary>Live members left where they are because they live in another book.</summary>
    public required int MembersSkipped { get; set; }
}
