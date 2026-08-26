namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Undo of <see cref="ContactMarkedDeceased"/> (recorded in error).</summary>
public sealed record ContactDeceasedCleared(Guid ContactId,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
