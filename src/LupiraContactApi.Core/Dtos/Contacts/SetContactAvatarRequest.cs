namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Sets (or clears, when null/empty) a contact's avatar — a URL/media id, never image bytes.</summary>
public sealed class SetContactAvatarRequest
{
    public string? AvatarRef { get; set; }
    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution. Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

}
