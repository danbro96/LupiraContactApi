namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Sets (or clears, when null/empty) a contact's avatar — a URL/media id, never image bytes.</summary>
public sealed class SetContactAvatarRequest
{
    public string? AvatarRef { get; set; }
}
