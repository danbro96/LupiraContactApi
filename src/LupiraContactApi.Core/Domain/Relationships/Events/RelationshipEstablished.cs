using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Relationships.Events;

/// <summary>"<paramref name="High"/> is <paramref name="Low"/>'s <paramref name="Kind"/>" now holds. Re-establishing a removed
/// relationship starts it afresh on the same stream: no labels, not ended.</summary>
public sealed record RelationshipEstablished(Guid RelationshipId, Guid Low, Guid High, ContactRelationKind Kind, DateOnly? Since, string? Note);
