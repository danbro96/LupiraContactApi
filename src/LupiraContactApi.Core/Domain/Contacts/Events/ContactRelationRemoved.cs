using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>The edge was a mistake and is erased. A relationship that ran its course is <see cref="ContactRelationEnded"/> instead.</summary>
public sealed record ContactRelationRemoved(Guid ContactId, Guid ToContactId, ContactRelationKind Kind);
