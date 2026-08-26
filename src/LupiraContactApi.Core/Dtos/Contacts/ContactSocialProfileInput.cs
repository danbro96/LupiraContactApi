namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>A social/IM handle as submitted. <c>Url</c> is derived from the handle for well-known services when
/// omitted; <c>Preferred</c> defaults to false.</summary>
public sealed class ContactSocialProfileInput
{
    public required string Service { get; set; }
    public required string Handle { get; set; }
    public string? Url { get; set; }
    public bool Preferred { get; set; }
}
