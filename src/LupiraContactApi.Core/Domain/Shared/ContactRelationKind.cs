using System.Text.Json.Serialization;
using LupiraContactApi.Core.Domain.Inference;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>Kind of a contact-to-contact edge: the related (To) contact's role relative to the owning contact ("To is my Kind").
/// The extended-family kinds are storable for when the linking relative isn't a contact (a deceased parent, say); the same
/// kinds are also produced by <see cref="KinshipInference"/> from the parent/child graph, distinguished on read by <see cref="RelationProvenance"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactRelationKind>))]
public enum ContactRelationKind
{
    Parent,
    Child,
    Sibling,
    Spouse,
    Partner,
    Friend,
    Colleague,
    Neighbor,
    Other,
    Grandparent,
    Grandchild,
    AuntUncle,
    NieceNephew,
    Cousin,
}
