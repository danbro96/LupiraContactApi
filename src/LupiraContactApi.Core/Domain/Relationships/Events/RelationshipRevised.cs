namespace LupiraContactApi.Core.Domain.Relationships.Events;

/// <summary>The shared fields, replaced wholesale.</summary>
public sealed record RelationshipRevised(Guid RelationshipId, DateOnly? Since, string? Note);
