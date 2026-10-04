using Lupira.Primitives;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Relationships;

/// <summary>Side-independent identity of a relationship: "<see cref="High"/> is <see cref="Low"/>'s <see cref="Kind"/>", the same
/// from either contact. Low/High is the ids' ordinal string order — never which side stated it.</summary>
public readonly record struct RelationshipKey(Guid Low, Guid High, ContactRelationKind Kind)
{
    /// <summary>The relationship's stream id — the same key always lands on the same stream, so a relationship exists once.</summary>
    public Guid StreamId => DeterministicGuid.From($"relationship:{Low:D}:{High:D}:{Kind}");

    /// <summary>The key of "<paramref name="otherId"/> is <paramref name="selfId"/>'s <paramref name="kind"/>".</summary>
    public static RelationshipKey Of(Guid selfId, Guid otherId, ContactRelationKind kind) =>
        string.CompareOrdinal(selfId.ToString("D"), otherId.ToString("D")) <= 0
            ? new(selfId, otherId, kind)
            : new(otherId, selfId, kind.Inverse());

    public Guid OtherThan(Guid contactId) => contactId == Low ? High : Low;

    /// <summary>The other contact's role relative to <paramref name="contactId"/>.</summary>
    public ContactRelationKind KindSeenFrom(Guid contactId) => contactId == Low ? Kind : Kind.Inverse();
}
