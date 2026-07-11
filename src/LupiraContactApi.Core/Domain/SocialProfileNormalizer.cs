namespace LupiraContactApi.Domain;

/// <summary>Canonicalizes a social/IM handle: service lowercased, handle trimmed (leading '@' stripped, except matrix
/// where it is part of the id), and a profile URL derived for well-known services when none was given.
/// Unknown services pass through untouched. Discord/mastodon get no URL (discriminators / instance-dependent).</summary>
public static class SocialProfileNormalizer
{
    private static readonly Dictionary<string, string> UrlTemplates = new(StringComparer.Ordinal)
    {
        ["telegram"] = "https://t.me/{0}",
        ["messenger"] = "https://m.me/{0}",
        ["facebook"] = "https://m.me/{0}",
        ["whatsapp"] = "https://wa.me/{0}",
        ["signal"] = "https://signal.me/#p/{0}",
        ["instagram"] = "https://instagram.com/{0}",
        ["linkedin"] = "https://www.linkedin.com/in/{0}",
        ["matrix"] = "https://matrix.to/#/{0}",
        ["x"] = "https://x.com/{0}",
        ["twitter"] = "https://x.com/{0}",
        ["github"] = "https://github.com/{0}",
    };

    public static ContactSocialProfile Normalize(ContactSocialProfile p)
    {
        var service = p.Service.Trim().ToLowerInvariant();
        var handle = service == "matrix" ? p.Handle.Trim() : p.Handle.Trim().TrimStart('@');
        var url = string.IsNullOrWhiteSpace(p.Url) ? DeriveUrl(service, handle) : p.Url.Trim();
        return new ContactSocialProfile { Service = service, Handle = handle, Url = url, Preferred = p.Preferred };
    }

    public static string? DeriveUrl(string service, string handle) =>
        handle.Length > 0 && UrlTemplates.TryGetValue(service, out var template) ? string.Format(template, handle) : null;
}
