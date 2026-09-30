using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Relationships;

/// <summary>A relationship as seen from one of its contacts. <see cref="Kind"/> is the other contact's role relative to the viewer and
/// <see cref="Label"/> the viewer's own name for them; the rest belongs to the relationship and reads the same from both sides.</summary>
public sealed record ResolvedRelation(
    Guid OtherId, ContactRelationKind Kind, string? Label, DateOnly? Since, string? Note, bool Ended, DateOnly? Until);
