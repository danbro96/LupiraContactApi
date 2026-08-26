using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts;

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
