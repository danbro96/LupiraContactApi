using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Computed social circles around a focus contact. Always contains every <see cref="CircleKind"/>, possibly empty;
/// a contact may appear in several circles.</summary>
public sealed class ContactCirclesDto
{
    public required Guid FocusContactId { get; set; }
    public required IReadOnlyList<ContactCircleDto> Circles { get; set; }
}

public sealed class ContactCircleDto
{
    public required CircleKind Kind { get; set; }
    public required IReadOnlyList<CircleMemberDto> Members { get; set; }
}

/// <summary><c>Degree</c> is a pragmatic closeness bucket (1 = immediate, 2 = two-generation kin, 3 = cousin), not
/// consanguinity. <c>Kind</c> is null when the membership makes no kinship claim (household co-residency).</summary>
public sealed class CircleMemberDto
{
    public required Guid ContactId { get; set; }
    public required string DisplayName { get; set; }
    public ContactRelationKind? Kind { get; set; }
    public required int Degree { get; set; }
    public required RelationProvenance Provenance { get; set; }
}
