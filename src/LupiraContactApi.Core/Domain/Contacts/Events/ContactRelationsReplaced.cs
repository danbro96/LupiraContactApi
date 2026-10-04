namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Replaced a contact's relation copies wholesale. Legacy: relationships are their own aggregate now (<see cref="Relationships.Relationship"/>); kept so old contact streams still deserialize, and read only by the relationships migration.</summary>
public sealed record ContactRelationsReplaced(Guid ContactId, IReadOnlyList<ContactRelation> Relations);
