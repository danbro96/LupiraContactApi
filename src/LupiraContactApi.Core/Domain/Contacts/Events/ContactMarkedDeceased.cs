namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>The person died. Deceased contacts stay in the kinship graph — death is not deletion. Date may be unknown.</summary>
public sealed record ContactMarkedDeceased(Guid ContactId, DateOnly? DeathDate,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
