namespace LupiraContactApi.Core.Dtos.Contacts;

public sealed class LookupContactsRequest
{
    public required List<Guid> ContactIds { get; set; }
}
