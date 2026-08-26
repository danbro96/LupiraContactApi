using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Inference;

/// <summary>One computed circle membership around a focus contact. <c>Degree</c> is a pragmatic closeness bucket
/// (1 = immediate, 2 = two-generation kin, 3 = cousin) — not consanguinity. <c>Kind</c> is null when the membership
/// makes no kinship claim (household co-residency).</summary>
public readonly record struct CircleMembership(CircleKind Circle, Guid ContactId, ContactRelationKind? Kind, int Degree, RelationProvenance Provenance);
