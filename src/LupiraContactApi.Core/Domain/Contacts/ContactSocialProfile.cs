namespace LupiraContactApi.Core.Domain.Contacts;

/// <summary>A social/IM handle. <c>Service</c> is an open string (platforms are unbounded); <c>Preferred</c> marks
/// the handle that actually reaches the person on that service.</summary>
public sealed class ContactSocialProfile
{
    public string Service { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;

    public string? Url { get; set; }

    public bool Preferred { get; set; }
}
