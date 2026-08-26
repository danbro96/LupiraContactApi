namespace LupiraContactApi.Core.Dtos.Internal;

public sealed class ContactSummaryDto
{
    public required Guid ContactId { get; set; }
    public required string DisplayName { get; set; }
}
