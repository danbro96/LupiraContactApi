namespace LupiraContactApi.Core.Dtos.Contacts;

public sealed class ContactMoveResult
{
    public required Guid ContactId { get; set; }

    public required ContactMoveOutcome Outcome { get; set; }
}
