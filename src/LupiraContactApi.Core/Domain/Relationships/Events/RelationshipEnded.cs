namespace LupiraContactApi.Core.Domain.Relationships.Events;

/// <summary>The relationship ran its course (divorce, falling-out) — it stays, flagged. Removal is for mistakes.</summary>
public sealed record RelationshipEnded(Guid RelationshipId, DateOnly? Until);
