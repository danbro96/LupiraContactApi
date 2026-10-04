using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Upserted a relation copy keyed by (ToContactId, Kind). Legacy: relationships are their own aggregate now (<see cref="Relationships.Relationship"/>); kept so old contact streams still deserialize, and read only by the relationships migration.</summary>
public sealed record ContactRelationAdded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string? Label, DateOnly? Since = null, string? Note = null);
