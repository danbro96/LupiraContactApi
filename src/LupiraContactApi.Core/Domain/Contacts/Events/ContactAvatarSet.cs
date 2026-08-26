namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Sets (or clears, when null) the avatar reference — a URL/media id, never image bytes. Outside the
/// canonical content like postal addresses, so it does not move the ETag.</summary>
public sealed record ContactAvatarSet(Guid ContactId, string? Ref,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
