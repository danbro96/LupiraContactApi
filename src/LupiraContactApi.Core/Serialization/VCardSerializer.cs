using System.Globalization;
using System.Text;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Serialization;

/// <summary>Minimal, deterministic vCard 3.0 writer + line-based parser — the only place vCard vocabulary is spoken.
/// Structured fields are canonical: GET regenerates the card from the snapshot, and the contact's <c>ContentHash</c>
/// (computed from domain state, not from these bytes) serves as the ETag. Full FolkerKinzel.VCards round-trip is a later step.</summary>
public static class VCardSerializer
{
    /// <summary>Regenerate the vCard for a contact from its structured fields and its relationships as seen from it
    /// (organisation lives on a ContactGroup, so it's omitted).</summary>
    public static string From(Contact c, IReadOnlyList<ResolvedRelation> relations) =>
        Build(c.ExternalId, ComposeFullName(c.GivenName, c.MiddleName, c.FamilyName, c.Nickname),
            c.GivenName, c.FamilyName, null, c.Channels, c.Birthday, relations,
            c.EmergencyContactIds, c.Profiles, c.Deceased, c.DeathDate, c.Notes, c.Pronouns, c.AvatarRef, c.Kind, c.MiddleName, c.Nickname);

    /// <summary>The vCard <c>FN</c>: the name parts joined, else the nickname, else empty.</summary>
    public static string ComposeFullName(string? given, string? middle, string? family, string? nickname)
    {
        var name = string.Join(' ', new[] { given, middle, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length > 0 ? name : (nickname ?? string.Empty);
    }

    public static string Build(
        string uid, string fullName, string? given, string? family, string? organization,
        IReadOnlyList<ContactReachChannel>? channels, PartialDate? birthday,
        IReadOnlyList<ResolvedRelation>? relations = null,
        IReadOnlyList<Guid>? emergencyContacts = null,
        IReadOnlyList<ContactSocialProfile>? profiles = null,
        bool deceased = false, DateOnly? deathDate = null,
        string? notes = null, string? pronouns = null, string? avatarRef = null,
        ContactKind kind = ContactKind.Individual, string? middle = null, string? nickname = null)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:3.0\r\n");
        sb.Append("UID:").Append(Escape(uid)).Append("\r\n");
        sb.Append("FN:").Append(Escape(fullName)).Append("\r\n");
        sb.Append("N:").Append(Escape(family ?? string.Empty)).Append(';').Append(Escape(given ?? string.Empty))
            .Append(';').Append(Escape(middle ?? string.Empty)).Append(";;\r\n");
        if (!string.IsNullOrWhiteSpace(nickname)) sb.Append("NICKNAME:").Append(Escape(nickname)).Append("\r\n");
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
            // Params are never quoted in this writer, so a label with param-breaking chars is dropped (kept in the store, lost on this surface only).
            if (r.Label is { Length: > 0 } label && IsSafeParamValue(label)) sb.Append(";X-LUPIRA-LABEL=").Append(label);
            if (r.Since is { } since) sb.Append(";X-LUPIRA-SINCE=").Append(since.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            if (r.Until is { } until) sb.Append(";X-LUPIRA-UNTIL=").Append(until.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            else if (r.Ended) sb.Append(";X-LUPIRA-ENDED=1");
            sb.Append(":urn:uuid:").Append(r.OtherId.ToString("D")).Append("\r\n");
        }

        foreach (var id in emergencyContacts ?? [])
            sb.Append("RELATED;TYPE=emergency:urn:uuid:").Append(id.ToString("D")).Append("\r\n");
        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    private static bool IsSafeParamValue(string s) => s.Length > 0 && s.All(ch => ch is not (';' or ':' or ',' or '"') && !char.IsControl(ch));

    /// <summary>Parses one card after unfolding lines and normalizing what the field mapping doesn't read: 2.1 quoted-printable/CHARSET
    /// values and bare params, property groups (<c>item1.TEL</c> with its <c>X-ABLabel</c>), 4.0 <c>PREF=1</c> and <c>tel:</c> URIs, timestamped or year-omitted BDAY.</summary>
    public static ParsedContact ParseVCard(string raw) => ParseCard([.. LogicalLines(raw)]);

    /// <summary>Splits a contacts file (vCard 2.1/3.0/4.0, one or more cards) and parses each card as <see cref="ParseVCard"/> does.</summary>
    /// <exception cref="FormatException">The file holds no card, or a card is unbalanced.</exception>
    public static IReadOnlyList<ParsedContact> ParseAll(string raw)
    {
        var cards = new List<ParsedContact>();
        var card = new List<string>();
        var depth = 0;
        foreach (var line in LogicalLines(raw))
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (depth++ == 0) card.Clear();
            }
            else if (trimmed.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0) throw new FormatException($"Card {cards.Count + 1} ends without a start.");
                if (--depth == 0) cards.Add(ParseCard(card));
            }
            else if (depth == 1)
            {
                card.Add(line);   // depth > 1 is a 2.1 AGENT card nested in this one: skipped
            }
        }

        if (depth > 0) throw new FormatException($"Card {cards.Count + 1} is not terminated.");
        if (cards.Count == 0) throw new FormatException("The file holds no contact cards.");
        return cards;
    }

    internal static Encoding CharsetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(charset.Trim()) ?? Encoding.GetEncoding(charset.Trim());
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static ParsedContact ParseCard(IReadOnlyList<string> lines)
    {
        string? fn = null, org = null, given = null, middle = null, family = null, nickname = null, notes = null, pronouns = null, uid = null;
        PartialDate? bday = null;
        DateOnly? deathDate = null;
        bool? deceased = null;
        ContactKind? kind = null;
        var channels = new List<ContactReachChannel>();
        var relations = new List<ResolvedRelation>();
        List<Guid>? emergency = null;
        List<ContactSocialProfile>? profiles = null;

        var groupLabels = GroupLabels(lines);
        foreach (var line in lines)
        {
            var l = NormalizeLine(line, groupLabels);
            var colon = ValueStart(l);
            if (colon < 0) continue;
            var prop = l[..colon].Split(';')[0].ToUpperInvariant();
            var val = l[(colon + 1)..];
            switch (prop)
            {
                case "UID": uid = Unescape(val).Trim() is { Length: > 0 } id ? id : null; break;
                case "FN": fn = Unescape(val); break;
                case "ORG": org = Unescape(val.Split(';')[0]); break;
                case "N":
                    var parts = val.Split(';');
                    if (parts.Length > 0) family = Unescape(parts[0]);
                    if (parts.Length > 1) given = Unescape(parts[1]);
                    if (parts.Length > 2) middle = Unescape(parts[2]).Trim() is { Length: > 0 } m ? m : null;
                    break;
                case "NICKNAME": nickname = Unescape(val).Trim(); break;
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
                case "X-ADDRESSBOOKSERVER-KIND": // the vCard 3.0-era convention for the same fact
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
                    else if (ParseRelated(p, val) is { } rel)
                    {
                        relations.Add(rel);
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(fn)) fn = string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ParsedContact(fn ?? string.Empty, given, family, org,
            channels.Count > 0 ? [.. channels] : null, bday,
            relations.Count > 0 ? [.. relations] : null,
            emergency?.ToArray(), profiles?.ToArray(), deceased, deathDate, notes, pronouns, kind, middle, nickname, uid);
    }

    // RFC folding (CRLF + space/tab) and 2.1 quoted-printable soft breaks (trailing '=') both continue a logical line.
    private static IEnumerable<string> LogicalLines(string raw)
    {
        StringBuilder? current = null;
        var quotedPrintable = false;
        foreach (var physical in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (current is { Length: > 0 } && quotedPrintable && current[^1] == '=')
            {
                current.Length--;
                current.Append(physical);
            }
            else if (current is not null && physical.Length > 0 && physical[0] is ' ' or '\t')
            {
                current.Append(physical, 1, physical.Length - 1);
            }
            else
            {
                if (current is not null) yield return current.ToString();
                current = new StringBuilder(physical);
                var colon = ValueStart(physical);
                quotedPrintable = physical.AsSpan(0, colon < 0 ? physical.Length : colon).Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (current is not null) yield return current.ToString();
    }

    // Apple writes a channel's label as a sibling X-ABLabel in the same property group, often after the channel line.
    private static Dictionary<string, string> GroupLabels(IReadOnlyList<string> lines)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var colon = ValueStart(line);
            if (colon < 0) continue;
            var name = SplitParams(line[..colon])[0];
            var dot = name.LastIndexOf('.');
            if (dot > 0 && name[(dot + 1)..].Equals("X-ABLABEL", StringComparison.OrdinalIgnoreCase)
                && ChannelLabelType(Unescape(line[(colon + 1)..])) is { } type)
                labels[name[..dot]] = type;
        }

        return labels;
    }

    private static string? ChannelLabelType(string label)
    {
        var text = label.Trim();
        if (text.StartsWith("_$!<", StringComparison.Ordinal) && text.EndsWith(">!$_", StringComparison.Ordinal)) text = text[4..^4];
        var type = text.ToLowerInvariant() switch
        {
            "mobile" or "iphone" => "cell",
            "homefax" or "workfax" or "otherfax" => "fax",
            var t => t,
        };
        return IsSafeParamValue(type) ? type : null;
    }

    private static string NormalizeLine(string line, IReadOnlyDictionary<string, string> groupLabels)
    {
        var colon = ValueStart(line);
        if (colon < 0) return line;
        var segments = SplitParams(line[..colon]);
        var dot = segments[0].LastIndexOf('.');
        var name = segments[0][(dot + 1)..].ToUpperInvariant();
        var value = line[(colon + 1)..];
        var kept = new List<string>();
        var types = new List<string>();
        string? encoding = null, charset = null, omitYear = null;
        var uri = false;
        foreach (var segment in segments.Skip(1))
        {
            var eq = segment.IndexOf('=');
            var key = eq < 0 ? BareParamKey(segment) : segment[..eq].Trim().ToUpperInvariant();
            var val = (eq < 0 ? segment : segment[(eq + 1)..]).Trim().Trim('"');
            switch (key)
            {
                case "TYPE": types.AddRange(val.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)); break;
                case "PREF": if (val == "1") types.Add("pref"); break;   // 4.0 ranks 1..100; only the top rank is "preferred"
                case "ENCODING": encoding = val; break;
                case "CHARSET": charset = val; break;
                case "X-APPLE-OMIT-YEAR": omitYear = val; break;
                case "VALUE":
                    uri = val.Equals("uri", StringComparison.OrdinalIgnoreCase);
                    kept.Add(segment);
                    break;
                default: kept.Add(segment); break;
            }
        }

        if (string.Equals(encoding, "QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            value = DecodeQuotedPrintable(value, CharsetEncoding(charset)).Replace("\r\n", "\\n").Replace('\r', '\n').Replace("\n", "\\n");
        switch (name)
        {
            case "TEL" or "EMAIL":
                types.RemoveAll(t => t.ToLowerInvariant() is "voice" or "internet" or "x400");   // the medium itself, not a type
                types = [.. types.Select(t => t.Equals("iphone", StringComparison.OrdinalIgnoreCase) ? "cell" : t)];
                if (uri && value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) value = value[4..];
                if (dot > 0 && groupLabels.TryGetValue(segments[0][..dot], out var label))
                {
                    types.RemoveAll(t => !t.Equals("pref", StringComparison.OrdinalIgnoreCase));
                    types.Insert(0, label);
                }

                break;
            case "BDAY":
                if (value.IndexOf('T') is > 0 and var t) value = value[..t];
                if (omitYear is { Length: > 0 } && value.StartsWith(omitYear, StringComparison.Ordinal)) value = "--" + value[omitYear.Length..].TrimStart('-');
                break;
        }

        var sb = new StringBuilder(name);
        foreach (var segment in kept) sb.Append(';').Append(segment);
        if (types.Count > 0) sb.Append(";TYPE=").Append(string.Join(',', types));
        return sb.Append(':').Append(value).ToString();
    }

    // vCard 2.1 writes param values without their names (TEL;CELL;PREF, NOTE;QUOTED-PRINTABLE).
    private static string BareParamKey(string token) =>
        token.Trim().ToUpperInvariant() is "QUOTED-PRINTABLE" or "BASE64" or "8BIT" or "7BIT" ? "ENCODING" : "TYPE";

    private static int ValueStart(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) return i;
        }

        return -1;
    }

    private static List<string> SplitParams(string nameAndParams)
    {
        var segments = new List<string>();
        var quoted = false;
        var start = 0;
        for (var i = 0; i < nameAndParams.Length; i++)
        {
            if (nameAndParams[i] == '"') quoted = !quoted;
            else if (nameAndParams[i] == ';' && !quoted)
            {
                segments.Add(nameAndParams[start..i]);
                start = i + 1;
            }
        }

        segments.Add(nameAndParams[start..]);
        return segments;
    }

    private static string DecodeQuotedPrintable(string value, Encoding encoding)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length
                && byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else
            {
                var width = char.IsHighSurrogate(value[i]) && i + 1 < value.Length ? 2 : 1;
                bytes.AddRange(encoding.GetBytes(value.ToCharArray(i, width)));
                i += width - 1;
            }
        }

        return encoding.GetString([.. bytes]);
    }

    // EMAIL/TEL → reach channel: TYPE tokens (comma-joined or repeated params) yield the first non-pref type + a pref flag.
    private static ContactReachChannel ParseChannel(ReachMedium medium, string nameAndParams, string val)
    {
        var types = nameAndParams.Split(';').Skip(1)
            .Where(p => p.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p[5..].Split(','))
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        var preferred = types.Any(x => x.Equals("pref", StringComparison.OrdinalIgnoreCase));
        var type = types.FirstOrDefault(x => !x.Equals("pref", StringComparison.OrdinalIgnoreCase));
        return new ContactReachChannel(medium, val, string.IsNullOrEmpty(type) ? null : type.ToLowerInvariant(), preferred);
    }

    private static DateOnly? ParseDate(string val)
    {
        if (DateOnly.TryParse(val, CultureInfo.InvariantCulture, out var d1)) return d1;
        if (DateOnly.TryParseExact(val, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;
        return null;
    }

    // Only urn:uuid targets are ours; RELATED lines pointing at URLs/free text from other clients are dropped.
    private static Guid? ParseUuidTarget(string val)
    {
        const string urnPrefix = "urn:uuid:";
        return val.StartsWith(urnPrefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(val[urnPrefix.Length..], out var target)
            ? target : null;
    }

    // The card's view of a relationship; the note is not carried on this surface.
    private static ResolvedRelation? ParseRelated(Dictionary<string, string> p, string val)
    {
        if (ParseUuidTarget(val) is not { } target) return null;
        var label = p.GetValueOrDefault("X-LUPIRA-LABEL");
        var since = p.TryGetValue("X-LUPIRA-SINCE", out var s) ? ParseDate(s) : null;
        var until = p.TryGetValue("X-LUPIRA-UNTIL", out var u) ? ParseDate(u) : null;
        return new ResolvedRelation(target, ParseRelationKind(p.GetValueOrDefault("TYPE")), string.IsNullOrEmpty(label) ? null : label,
            since, Note: null, Ended: until is not null || p.ContainsKey("X-LUPIRA-ENDED"), until);
    }

    private static ContactSocialProfile? ParseSocialProfile(string nameAndParams, string val)
    {
        var p = Params(nameAndParams);
        var service = p.GetValueOrDefault("TYPE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(service) || val.Length == 0) return null;

        var isUrl = val.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        var handle = isUrl ? val.TrimEnd('/').Split('/').LastOrDefault(s => s.Length > 0) ?? val : val;
        return new ContactSocialProfile { Service = service, Handle = handle, Url = isUrl ? val : null, Preferred = p.ContainsKey("X-LUPIRA-PREF") };
    }

    private static Dictionary<string, string> Params(string nameAndParams)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in nameAndParams.Split(';').Skip(1))
        {
            var eq = param.IndexOf('=');
            if (eq > 0) map[param[..eq]] = param[(eq + 1)..];
        }

        return map;
    }

    private static ContactRelationKind ParseRelationKind(string? type)
    {
        if (Enum.TryParse<ContactRelationKind>(type, true, out var kind)) return kind;
        return type?.ToLowerInvariant() switch
        {
            "co-worker" => ContactRelationKind.Colleague,
            "sweetheart" => ContactRelationKind.Partner,
            _ => ContactRelationKind.Other,   // incl. missing TYPE and RFC values without a member (kin, muse, ...)
        };
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");

    private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
