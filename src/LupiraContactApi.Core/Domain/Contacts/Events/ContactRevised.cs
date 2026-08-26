namespace LupiraContactApi.Core.Domain.Contacts.Events;

public sealed record ContactRevised(Guid ContactId, ContactFields Fields,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
