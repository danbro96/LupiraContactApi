namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Replaces the contact's social/IM handles.</summary>
public sealed record ContactProfilesReplaced(Guid ContactId, IReadOnlyList<ContactSocialProfile> Profiles,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
