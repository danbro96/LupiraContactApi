namespace LupiraContactApi.Domain;

/// <summary>A typed, directed relation edge embedded in the owning contact's snapshot: "the To contact is my Kind".
/// Keyed by (ToContactId, Kind); <c>Label</c> is a free-text refinement ("dad"). <c>Ended</c>/<c>Until</c> mark a
/// relationship that ran its course (ex-spouse) — distinct from removal, which means the edge was a mistake.
/// No FK — the target may be deleted or unreadable; resolved read surfaces filter.</summary>
public sealed class ContactRelation
{
    public Guid ToContactId { get; set; }
    public ContactRelationKind Kind { get; set; }
    public string? Label { get; set; }
    public bool Ended { get; set; }
    public DateOnly? Until { get; set; }
}

/// <summary>Derives the kind seen from the other side of an edge (the incoming view).</summary>
public static class ContactRelationKinds
{
    public static ContactRelationKind Inverse(this ContactRelationKind kind) => kind switch
    {
        ContactRelationKind.Parent => ContactRelationKind.Child,
        ContactRelationKind.Child => ContactRelationKind.Parent,
        _ => kind,   // remaining kinds are symmetric
    };

    /// <summary>Widen a stored kind to the read-model <see cref="KinshipKind"/>; the two enums share leading ordinals.</summary>
    public static KinshipKind AsKinship(this ContactRelationKind kind) => (KinshipKind)(int)kind;
}
