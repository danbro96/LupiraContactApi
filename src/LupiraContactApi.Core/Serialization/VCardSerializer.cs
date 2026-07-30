using System.Globalization;
using System.Text;
using LupiraContactApi.Domain;

namespace LupiraContactApi.Serialization;

/// <summary>Minimal, deterministic vCard 3.0 writer + line-based parser — the only place vCard vocabulary is spoken.
/// Structured fields are canonical: GET regenerates the card from the snapshot, and the contact's <c>ContentHash</c>
/// (computed from domain state, not from these bytes) serves as the ETag. Full FolkerKinzel.VCards round-trip is a later step.</summary>
public static class VCardSerializer
{
    /// <summary>Regenerate the vCard for a contact from its structured fields (organisation lives on a ContactGroup, so it's omitted).</summary>
    public static string From(Contact c) =>
        Build(c.ExternalId, ComposeFullName(c.GivenName, c.MiddleName, c.FamilyName, c.Nickname),
            c.GivenName, c.FamilyName, null, c.Channels, c.Birthday, c.Relations,
            c.EmergencyContactIds, c.Profiles, c.Deceased, c.DeathDate, c.Notes, c.Pronouns, c.AvatarRef, c.Kind);

    /// <summary>The vCard <c>FN</c>: the name parts joined, else the nickname, else empty.</summary>
    public static string ComposeFullName(string? given, string? middle, string? family, string? nickname)
    {
        var name = string.Join(' ', new[] { given, middle, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length > 0 ? name : (nickname ?? "");
    }

    public static string Build(
        string uid, string fullName, string? given, string? family, string? organization,
        IReadOnlyList<ContactReachChannel>? channels, PartialDate? birthday,
        IReadOnlyList<ContactRelation>? relations = null,
        IReadOnlyList<Guid>? emergencyContacts = null,
        IReadOnlyList<ContactSocialProfile>? profiles = null,
        bool deceased = false, DateOnly? deathDate = null,
        string? notes = null, string? pronouns = null, string? avatarRef = null,
        ContactKind kind = ContactKind.Individual)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:3.0\r\n");
        sb.Append("UID:").Append(Escape(uid)).Append("\r\n");
        sb.Append("FN:").Append(Escape(fullName)).Append("\r\n");
        sb.Append("N:").Append(Escape(family ?? "")).Append(';').Append(Escape(given ?? "")).Append(";;;\r\n");
        if (kind == ContactKind.Organization) sb.Append("KIND:org\r\n");   // vCard 4.0 property; individual is the implied default
        if (!string.IsNullOrWhiteSpace(organization)) sb.Append("ORG:").Append(Escape(organization)).Append("\r\n");
        foreach (var ch in channels ?? [])
        {
            sb.Append(ch.Medium == ReachMedium.Email ? "EMAIL" : "TEL");
            var types = new List<string>();
            if (ch.Type is { Length: > 0 } t && IsSafeParamValue(t)) types.Add(t);
            if (ch.Preferred) types.Add("pref");
            if (types.Count > 0) sb.Append(";TYPE=").Append(string.Join(',', types));
            sb.Append(':').Append(Escape(ch.Value)).Append("\r\n");
        }

        if (birthday is { } b) sb.Append("BDAY:").Append(b.Year is { } by ? $"{by:D4}{b.Month:D2}{b.Day:D2}" : $"--{b.Month:D2}{b.Day:D2}").Append("\r\n");
        if (deathDate is { } dd) sb.Append("X-DEATHDATE:").Append(dd.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
        else if (deceased) sb.Append("X-LUPIRA-DECEASED:1\r\n");
        if (!string.IsNullOrWhiteSpace(notes)) sb.Append("NOTE:").Append(Escape(notes)).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(pronouns)) sb.Append("X-PRONOUNS:").Append(Escape(pronouns)).Append("\r\n");
        if (avatarRef is { Length: > 0 } && avatarRef.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            sb.Append("PHOTO;VALUE=uri:").Append(Escape(avatarRef)).Append("\r\n");   // URLs only — embedded image bytes are not stored
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
            if (r.Since is { } since) sb.Append(";X-LUPIRA-SINCE=").Append(since.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
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
        string? fn = null, org = null, given = null, family = null, notes = null, pronouns = null;
        PartialDate? bday = null;
        DateOnly? deathDate = null;
        bool? deceased = null;
        ContactKind? kind = null;
        var channels = new List<ContactReachChannel>();
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
                case "EMAIL": channels.Add(ParseChannel(ReachMedium.Email, l[..colon], Unescape(val))); break;
                case "TEL": channels.Add(ParseChannel(ReachMedium.Phone, l[..colon], Unescape(val))); break;
                case "BDAY": bday = PartialDate.Parse(val); break;
                case "NOTE": notes = Unescape(val) is { Length: > 0 } n ? n : null; break;
                case "X-PRONOUNS": pronouns = Unescape(val) is { Length: > 0 } pr ? pr : null; break;
                case "X-DEATHDATE":
                    deceased = true;
                    deathDate = ParseDate(val);   // unparsable date still means deceased
                    break;
                case "X-LUPIRA-DECEASED": deceased = true; break;
                case "KIND":
                case "X-ADDRESSBOOKSERVER-KIND":   // the vCard 3.0-era convention for the same fact
                    kind = val.Trim().ToLowerInvariant() is "org" or "organization" ? ContactKind.Organization : ContactKind.Individual;
                    break;
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
            channels.Count > 0 ? [.. channels] : null, bday,
            relations.Count > 0 ? [.. relations] : null,
            emergency?.ToArray(), profiles?.ToArray(), deceased, deathDate, notes, pronouns, kind);
    }

    // EMAIL/TEL → reach channel: TYPE tokens (comma-joined or repeated params) yield the first non-pref type + a pref flag.
    static ContactReachChannel ParseChannel(ReachMedium medium, string nameAndParams, string val)
    {
        var types = nameAndParams.Split(';').Skip(1)
            .Where(p => p.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p[5..].Split(','))
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        var preferred = types.Any(x => x.Equals("pref", StringComparison.OrdinalIgnoreCase));
        var type = types.FirstOrDefault(x => !x.Equals("pref", StringComparison.OrdinalIgnoreCase));
        return new ContactReachChannel(medium, val, string.IsNullOrEmpty(type) ? null : type.ToLowerInvariant(), preferred);
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
        var since = p.TryGetValue("X-LUPIRA-SINCE", out var s) ? ParseDate(s) : null;
        var until = p.TryGetValue("X-LUPIRA-UNTIL", out var u) ? ParseDate(u) : null;
        return new ContactRelation
        {
            ToContactId = target,
            Kind = ParseRelationKind(p.GetValueOrDefault("TYPE")),
            Label = string.IsNullOrEmpty(label) ? null : label,
            Since = since,
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
