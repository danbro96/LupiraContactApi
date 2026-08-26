using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>The relationship ended (divorce, falling-out) — the edge stays, flagged, with an optional end date.</summary>
public sealed record ContactRelationEnded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, DateOnly? Until);
