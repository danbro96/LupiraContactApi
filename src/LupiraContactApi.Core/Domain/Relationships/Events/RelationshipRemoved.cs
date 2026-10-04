namespace LupiraContactApi.Core.Domain.Relationships.Events;

/// <summary>The relationship was entered by mistake and is erased. The snapshot stays as a tombstone so sync feeds can report it.</summary>
public sealed record RelationshipRemoved(Guid RelationshipId);
