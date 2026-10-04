namespace LupiraContactApi.Core.Domain.Relationships.Events;

/// <summary><paramref name="ContactId"/>'s own word for the other contact; null clears it.</summary>
public sealed record RelationshipLabelled(Guid RelationshipId, Guid ContactId, string? Label);
