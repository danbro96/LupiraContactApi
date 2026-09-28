using LupiraContactApi.Core.Dtos.Contacts;

namespace LupiraContactApi.Core.Dtos.Internal;

public sealed class ContactBirthdaysResponse
{
    public required List<ContactBirthdayDto> Contacts { get; set; }
}
