using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Erased a relation copy. Legacy: relationships are their own aggregate now (<see cref="Relationships.Relationship"/>); kept so old contact streams still deserialize, and read only by the relationships migration.</summary>
public sealed record ContactRelationRemoved(Guid ContactId, Guid ToContactId, ContactRelationKind Kind);
