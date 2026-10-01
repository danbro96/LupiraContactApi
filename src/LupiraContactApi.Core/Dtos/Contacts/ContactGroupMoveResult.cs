namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>A moved group plus, when its members were moved too, each member's outcome.</summary>
public sealed class ContactGroupMoveResult
{
    public required ContactGroupDto Group { get; set; }

    public required List<ContactMoveResult> Members { get; set; }
}
