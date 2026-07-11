using System.Text.Json.Serialization;

namespace LupiraContactApi.Domain;

/// <summary>A personal grouping (Friends/Family/Colleagues) vs a company/institution. An employer is membership in an <c>Organization</c>-kind group.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactGroupKind>))]
public enum ContactGroupKind { Group, Organization }

/// <summary>Type of a contact's postal address.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactAddressType>))]
public enum ContactAddressType { Home, Work, Other }

/// <summary>Kind of a contact-to-contact edge: the related (To) contact's role relative to the owning contact ("To is my Kind").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactRelationKind>))]
public enum ContactRelationKind { Parent, Child, Sibling, Spouse, Partner, Friend, Colleague, Neighbor, Other }

/// <summary>Resolved role of a related contact on the read surface: every <see cref="ContactRelationKind"/> plus the
/// kinships derived from the parent/child graph (never stored — only returned when inferred relations are requested).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KinshipKind>))]
public enum KinshipKind { Parent, Child, Sibling, Spouse, Partner, Friend, Colleague, Neighbor, Other, Grandparent, Grandchild, AuntUncle, NieceNephew, Cousin }

/// <summary>Whether a resolved relation was stored explicitly or derived from the kinship graph.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RelationProvenance>))]
public enum RelationProvenance { Explicit, Inferred }

/// <summary>An inferred social cohort around a focus contact (computed on read, never stored).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CircleKind>))]
public enum CircleKind { CloseFamily, ExtendedFamily, Friends, Colleagues, Household }
