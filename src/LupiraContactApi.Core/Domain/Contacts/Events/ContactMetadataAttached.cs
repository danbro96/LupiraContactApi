namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Replaces the contact's annotation metadata (the merged JSON object — merge happens in the service).
/// Outside the canonical content like the avatar, so it does not move the ETag. Carries completeness N/A acknowledgments.</summary>
public sealed record ContactMetadataAttached(Guid ContactId, string MetadataJson,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
