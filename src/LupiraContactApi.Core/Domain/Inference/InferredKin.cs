using LupiraContactApi.Core.Domain.Completeness;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Inference;

/// <summary>A kinship derived from the parent/child graph (never stored). Pure over a supplied set of contacts,
/// like <see cref="CompletenessScorer"/>; the session-bound loading + access filtering lives in ContactService.</summary>
public readonly record struct InferredKin(Guid ContactId, ContactRelationKind Kind);
