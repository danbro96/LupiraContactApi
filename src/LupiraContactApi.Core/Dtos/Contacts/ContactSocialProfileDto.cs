namespace LupiraContactApi.Dtos.Contacts;

/// <summary>A contact's social/IM handle as published. Service and handle are guaranteed non-empty —
/// <c>SetProfilesAsync</c> rejects the write otherwise.</summary>
public sealed class ContactSocialProfileDto
{
    public required string Service { get; set; }
    public required string Handle { get; set; }
    public required string? Url { get; set; }
    public required bool Preferred { get; set; }
}
