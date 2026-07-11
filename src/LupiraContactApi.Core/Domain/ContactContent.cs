using System.Globalization;
using System.Text;

namespace LupiraContactApi.Domain;

/// <summary>Deterministic canonical text of a contact's content-bearing state; <c>ContentHash = ContentHash.Of(Canonical(...))</c>.
/// Wire formats are serialization concerns and play no part in identity or hashing; sync surfaces consume the hash as an
/// opaque version tag. Order-sensitive throughout: reordering profiles, relations, or emergency contacts is a real change.
/// Addresses are deliberately excluded (see <see cref="ContactAddressesReplaced"/>).</summary>
public static class ContactContent
{
    public static string Canonical(
        string externalId, ContactFields f,
        IReadOnlyList<ContactRelation> relations, IReadOnlyList<Guid> emergencyContactIds,
        IReadOnlyList<ContactSocialProfile> profiles, bool deceased, DateOnly? deathDate)
    {
        var sb = new StringBuilder();
        Line(sb, "id", externalId);
        Line(sb, "name", f.NamePrefix, f.GivenName, f.MiddleName, f.FamilyName, f.NameSuffix, f.Nickname);
        foreach (var ch in f.Channels ?? []) Line(sb, "channel", ch.Medium.ToString(), ch.Value, ch.Type, ch.Preferred ? "1" : "0");
        Line(sb, "birthday", f.Birthday?.ToCanonical());
        Line(sb, "notes", f.Notes);
        Line(sb, "pronouns", f.Pronouns);
        Line(sb, "deceased", deceased ? "1" : "0", Date(deathDate));
        foreach (var p in profiles) Line(sb, "profile", p.Service, p.Handle, p.Url, p.Preferred ? "1" : "0");
        foreach (var r in relations) Line(sb, "relation", r.ToContactId.ToString("D"), r.Kind.ToString(), r.Label, r.Ended ? "1" : "0", Date(r.Until), Date(r.Since), r.Note);
        foreach (var id in emergencyContactIds) Line(sb, "emergency", id.ToString("D"));
        return sb.ToString();
    }

    private static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static void Line(StringBuilder sb, string key, params string?[] values)
    {
        sb.Append(key);
        foreach (var v in values) sb.Append('|').Append(Esc(v));
        sb.Append('\n');
    }

    private static string Esc(string? s) =>
        s is null ? "" : s.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\n", "\\n");
}
