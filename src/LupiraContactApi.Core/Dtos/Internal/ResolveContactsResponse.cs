namespace LupiraContactApi.Core.Dtos.Internal;

public sealed class ResolveContactsResponse
{
    public required List<ContactSummaryDto> Contacts { get; set; }
}
