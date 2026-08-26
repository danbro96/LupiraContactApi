using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Computed social circles around a focus contact. Always contains every <see cref="CircleKind"/>, possibly empty;
/// a contact may appear in several circles.</summary>
public sealed class ContactCirclesDto
{
    public required Guid FocusContactId { get; set; }
    public required IReadOnlyList<ContactCircleDto> Circles { get; set; }
}
