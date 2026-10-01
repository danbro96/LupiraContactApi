namespace LupiraContactApi.Core.Dtos.Contacts;

public sealed class MoveContactGroupRequest
{
    /// <summary>The address book to move the group into.</summary>
    public required Guid AddressBookId { get; set; }

    /// <summary>Also move the member contacts that live in the group's current book; members in other books stay put.</summary>
    public bool IncludeMembers { get; set; }
}
