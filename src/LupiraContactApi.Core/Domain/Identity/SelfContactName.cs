namespace LupiraContactApi.Core.Domain.Identity;

/// <summary>Names the contact created for a principal that has none: the display name's first token is the given name and
/// the rest the family name; without a display name, the email's local part is the given name.</summary>
public static class SelfContactName
{
    public static (string? Given, string? Family) From(string? displayName, string email)
    {
        // Some logins carry the email as their name — that is no name at all.
        var name = string.Equals(displayName?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase) ? null : displayName;
        var tokens = (name ?? string.Empty).Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0) return (tokens[0], tokens.Length > 1 ? string.Join(' ', tokens[1..]) : null);

        var local = email.Split('@')[0].Trim();
        return (local.Length > 0 ? local : null, null);
    }
}
