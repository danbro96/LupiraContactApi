namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Replaces the ordered emergency-contact designation (order = priority). A designation, not a kinship.</summary>
public sealed record ContactEmergencyContactsReplaced(Guid ContactId, IReadOnlyList<Guid> ContactIds);
