using LupiraContactApi.Domain;
using System.Globalization;
using System.Text;

namespace LupiraContactApi.Serialization;

/// <summary>Minimal, deterministic vCard 3.0 writer + line-based parser — the only place vCard vocabulary is spoken.
/// Structured fields are canonical: GET regenerates the card from the snapshot, and the contact's <c>ContentHash</c>
/// (computed from domain state, not from these bytes) serves as the ETag. Full FolkerKinzel.VCards round-trip is a later step.</summary>
public static class VCardSerializer
{
    /// <summary>Regenerate the vCard for a contact from its structured fields (organisation lives on a ContactGroup, so it's omitted).</summary>
    public static string From(Contact c) =>
        Build(c.ExternalId, ComposeFullName(c.NamePrefix, c.GivenName, c.MiddleName, c.FamilyName, c.NameSuffix, c.Nickname),
            c.GivenName, c.FamilyName, null, c.Emails, c.Phones, c.Birthday, c.Relations,
            c.EmergencyContactIds, c.Profiles, c.Deceased, c.DeathDate);

    /// <summary>The vCard <c>FN</c>: the name parts joined, else the nickname, else empty.</summary>
    public static string ComposeFullName(string? prefix, string? given, string? middle, string? family, string? suffix, string? nickname)
    {
        var name = string.Join(' ', new[] { prefix, given, middle, family, suffix }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length > 0 ? name : (nickname ?? "");
    }

    public static string Build(
        string uid, string fullName, string? given, string? family, string? organization,
        IEnumerable<string>? emails, IEnumerable<string>? phones, DateOnly? birthday,
        IReadOnlyList<ContactRelation>? relations = null,
        IReadOnlyList<Guid>? emergencyContacts = null,
        IReadOnlyList<ContactSocialProfile>? profiles = null,
        bool deceased = false, DateOnly? deathDate = null)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:3.0\r\n");
        sb.Append("UID:").Append(Escape(uid)).Append("\r\n");
        sb.Append("FN:").Append(Escape(fullName)).Append("\r\n");
        sb.Append("N:").Append(Escape(family ?? "")).Append(';').Append(Escape(given ?? "")).Append(";;;\r\n");
        if (!string.IsNullOrWhiteSpace(organization)) sb.Append("ORG:").Append(Escape(organization)).Append("\r\n");
        foreach (var email in emails ?? []) sb.Append("EMAIL:").Append(Escape(email)).Append("\r\n");
        foreach (var phone in phones ?? []) sb.Append("TEL:").Append(Escape(phone)).Append("\r\n");
        if (birthday is { } b) sb.Append("BDAY:").Append(b.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
        if (deathDate is { } dd) sb.Append("X-DEATHDATE:").Append(dd.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
        else if (deceased) sb.Append("X-LUPIRA-DECEASED:1\r\n");
        foreach (var p in profiles ?? [])
        {
            if (!IsSafeParamValue(p.Service)) continue;   // params are never quoted in this writer
            sb.Append("X-SOCIALPROFILE;TYPE=").Append(p.Service);
            if (p.Preferred) sb.Append(";X-LUPIRA-PREF=1");
            sb.Append(':').Append(Escape(p.Url ?? p.Handle)).Append("\r\n");
        }
        foreach (var r in relations ?? [])
        {
            sb.Append("RELATED;TYPE=").Append(r.Kind.ToString().ToLowerInvariant());
            // Params are never quoted in this writer, so a label with param-breaking chars is dropped (survives in the snapshot, lost on this surface only).
            if (r.Label is { Length: > 0 } label && IsSafeParamValue(label)) sb.Append(";X-LUPIRA-LABEL=").Append(label);
            if (r.Until is { } until) sb.Append(";X-LUPIRA-UNTIL=").Append(until.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            else if (r.Ended) sb.Append(";X-LUPIRA-ENDED=1");
            sb.Append(":urn:uuid:").Append(r.ToContactId.ToString("D")).Append("\r\n");
        }
        foreach (var id in emergencyContacts ?? [])
            sb.Append("RELATED;TYPE=emergency:urn:uuid:").Append(id.ToString("D")).Append("\r\n");
        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    static bool IsSafeParamValue(string s) => s.Length > 0 && s.All(ch => ch is not (';' or ':' or ',' or '"') && !char.IsControl(ch));

    public static ParsedContact ParseVCard(string raw)
    {
        string? fn = null, org = null, given = null, family = null;
        DateOnly? bday = null, deathDate = null;
        bool? deceased = null;
        var emails = new List<string>();
        var phones = new List<string>();
        var relations = new List<ContactRelation>();
        List<Guid>? emergency = null;
        List<ContactSocialProfile>? profiles = null;

        foreach (var line in raw.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length == 0 || l[0] == ' ' || l[0] == '\t') continue;   // skip blanks + folded continuations
            var colon = l.IndexOf(':');
            if (colon < 0) continue;
            var prop = l[..colon].Split(';')[0].ToUpperInvariant();
            var val = l[(colon + 1)..];
            switch (prop)
            {
                case "FN": fn = Unescape(val); break;
                case "ORG": org = Unescape(val.Split(';')[0]); break;
                case "N":
                    var parts = val.Split(';');
                    if (parts.Length > 0) family = Unescape(parts[0]);
                    if (parts.Length > 1) given = Unescape(parts[1]);
                    break;
                case "EMAIL": emails.Add(Unescape(val)); break;
                case "TEL": phones.Add(Unescape(val)); break;
                case "BDAY": bday = ParseDate(val); break;
                case "X-DEATHDATE":
                    deceased = true;
                    deathDate = ParseDate(val);   // unparsable date still means deceased
                    break;
                case "X-LUPIRA-DECEASED": deceased = true; break;
                case "X-SOCIALPROFILE":
                    if (ParseSocialProfile(l[..colon], Unescape(val)) is { } sp) (profiles ??= []).Add(sp);
                    break;
                case "RELATED":
                    var p = Params(l[..colon]);
                    if (p.GetValueOrDefault("TYPE") is { } t && t.Equals("emergency", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ParseUuidTarget(val) is { } eid) (emergency ??= []).Add(eid);
                    }
                    else if (ParseRelated(p, val) is { } rel) relations.Add(rel);
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(fn)) fn = string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ParsedContact(fn ?? "", given, family, org,
            emails.Count > 0 ? [.. emails] : null, phones.Count > 0 ? [.. phones] : null, bday,
            relations.Count > 0 ? [.. relations] : null,
            emergency?.ToArray(), profiles?.ToArray(), deceased, deathDate);
    }

    static DateOnly? ParseDate(string val)
    {
        if (DateOnly.TryParse(val, CultureInfo.InvariantCulture, out var d1)) return d1;
        if (DateOnly.TryParseExact(val, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;
        return null;
    }

    // Only urn:uuid targets are ours; RELATED lines pointing at URLs/free text from other clients are dropped.
    static Guid? ParseUuidTarget(string val)
    {
        const string urnPrefix = "urn:uuid:";
        return val.StartsWith(urnPrefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(val[urnPrefix.Length..], out var target)
            ? target : null;
    }

    static ContactRelation? ParseRelated(Dictionary<string, string> p, string val)
    {
        if (ParseUuidTarget(val) is not { } target) return null;
        var label = p.GetValueOrDefault("X-LUPIRA-LABEL");
        var until = p.TryGetValue("X-LUPIRA-UNTIL", out var u) ? ParseDate(u) : null;
        return new ContactRelation
        {
            ToContactId = target,
            Kind = ParseRelationKind(p.GetValueOrDefault("TYPE")),
            Label = string.IsNullOrEmpty(label) ? null : label,
            Ended = until is not null || p.ContainsKey("X-LUPIRA-ENDED"),
            Until = until,
        };
    }

    static ContactSocialProfile? ParseSocialProfile(string nameAndParams, string val)
    {
        var p = Params(nameAndParams);
        var service = p.GetValueOrDefault("TYPE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(service) || val.Length == 0) return null;

        var isUrl = val.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        var handle = isUrl ? val.TrimEnd('/').Split('/').LastOrDefault(s => s.Length > 0) ?? val : val;
        return new ContactSocialProfile { Service = service, Handle = handle, Url = isUrl ? val : null, Preferred = p.ContainsKey("X-LUPIRA-PREF") };
    }

    static Dictionary<string, string> Params(string nameAndParams)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in nameAndParams.Split(';').Skip(1))
        {
            var eq = param.IndexOf('=');
            if (eq > 0) map[param[..eq]] = param[(eq + 1)..];
        }
        return map;
    }

    static ContactRelationKind ParseRelationKind(string? type)
    {
        if (Enum.TryParse<ContactRelationKind>(type, true, out var kind)) return kind;
        return type?.ToLowerInvariant() switch
        {
            "co-worker" => ContactRelationKind.Colleague,
            "sweetheart" => ContactRelationKind.Partner,
            _ => ContactRelationKind.Other,   // incl. missing TYPE and RFC values without a member (kin, muse, ...)
        };
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
    static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
