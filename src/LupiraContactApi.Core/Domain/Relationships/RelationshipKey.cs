using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Relationships;

/// <summary>Side-independent identity of a relationship: "<see cref="High"/> is <see cref="Low"/>'s <see cref="Kind"/>". Both stored
/// copies, seen from either contact, land on the same key. Ordered by the ids' ordinal string form, which the clients reproduce
/// from JSON ids (<c>@lupira/cal-domain/contactRelations</c>).</summary>
public readonly record struct RelationshipKey(Guid Low, Guid High, ContactRelationKind Kind)
{
    /// <summary>The key of "<paramref name="otherId"/> is <paramref name="selfId"/>'s <paramref name="kind"/>".</summary>
    public static RelationshipKey Of(Guid selfId, Guid otherId, ContactRelationKind kind) =>
        string.CompareOrdinal(selfId.ToString("D"), otherId.ToString("D")) <= 0
            ? new(selfId, otherId, kind)
            : new(otherId, selfId, kind.Inverse());

    public static RelationshipKey Of(RelationCopy copy) => Of(copy.HolderId, copy.Edge.ToContactId, copy.Edge.Kind);

    public Guid OtherThan(Guid contactId) => contactId == Low ? High : Low;

    /// <summary>The other contact's role relative to <paramref name="contactId"/>.</summary>
    public ContactRelationKind KindSeenFrom(Guid contactId) => contactId == Low ? Kind : Kind.Inverse();
}
