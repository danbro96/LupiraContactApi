using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

public sealed class ContactCircleDto
{
    public required CircleKind Kind { get; set; }
    public required IReadOnlyList<CircleMemberDto> Members { get; set; }
}
