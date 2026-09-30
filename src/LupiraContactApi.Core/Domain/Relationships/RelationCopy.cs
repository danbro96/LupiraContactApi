using LupiraContactApi.Core.Domain.Contacts;

namespace LupiraContactApi.Core.Domain.Relationships;

/// <summary>One stored copy of a relationship: the edge as held on <see cref="HolderId"/>'s stream. A relationship has at most one
/// copy per side; <see cref="RelationResolver"/> merges them, so no read reveals which side holds it.</summary>
public sealed record RelationCopy(Guid HolderId, ContactRelation Edge);
