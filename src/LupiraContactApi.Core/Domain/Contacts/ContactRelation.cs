using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts;

/// <summary>A relation copy as legacy contact events carried it: "the To contact is my Kind", keyed by (ToContactId, Kind).
/// Legacy: relationships are their own aggregate now (<see cref="Relationships.Relationship"/>); kept so old contact streams still deserialize, and read only by the relationships migration.</summary>
public sealed class ContactRelation
{
    public Guid ToContactId { get; set; }

    public ContactRelationKind Kind { get; set; }

    public string? Label { get; set; }

    /// <summary>When the relationship began, if a precise date is known (fuzzy periods belong in <see cref="Note"/>).</summary>
    public DateOnly? Since { get; set; }

    /// <summary>Free-text refinement of the edge itself (how/where it started), distinct from the kind-refining <see cref="Label"/>.</summary>
    public string? Note { get; set; }

    public bool Ended { get; set; }

    public DateOnly? Until { get; set; }
}
