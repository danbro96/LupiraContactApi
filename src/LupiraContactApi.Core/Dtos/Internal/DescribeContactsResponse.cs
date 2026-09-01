namespace LupiraContactApi.Core.Dtos.Internal;

public sealed class DescribeContactsResponse
{
    public required List<ContactDescriptionDto> Contacts { get; set; }
}
