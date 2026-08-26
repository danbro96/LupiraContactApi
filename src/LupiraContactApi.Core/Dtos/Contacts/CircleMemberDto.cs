using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

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
