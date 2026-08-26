namespace LupiraContactApi.Core.Domain;

/// <summary>A typed, directed relation edge embedded in the owning contact's snapshot: "the To contact is my Kind".
/// Keyed by (ToContactId, Kind); <c>Label</c> is a free-text refinement ("dad"). <c>Ended</c>/<c>Until</c> mark a
/// relationship that ran its course (ex-spouse) — distinct from removal, which means the edge was a mistake.
/// No FK — the target may be deleted or unreadable; resolved read surfaces filter.</summary>
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

/// <summary>Derives the kind seen from the other side of an edge (the incoming view).</summary>
public static class ContactRelationKinds
{
    public static ContactRelationKind Inverse(this ContactRelationKind kind) => kind switch
    {
        ContactRelationKind.Parent => ContactRelationKind.Child,
        ContactRelationKind.Child => ContactRelationKind.Parent,
        ContactRelationKind.Grandparent => ContactRelationKind.Grandchild,
        ContactRelationKind.Grandchild => ContactRelationKind.Grandparent,
        ContactRelationKind.AuntUncle => ContactRelationKind.NieceNephew,
        ContactRelationKind.NieceNephew => ContactRelationKind.AuntUncle,
        _ => kind,   // remaining kinds (incl. Sibling, Cousin) are symmetric
    };
}
