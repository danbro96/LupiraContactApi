using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LupiraContactApi.Domain;

/// <summary>A field is fully present, weak/partial (0.5), or absent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GapSeverity>))]
public enum GapSeverity { Weak, Absent }

/// <summary>A field the record is missing or thin on, with its rubric weight (heavier = ask first).</summary>
public sealed record CompletenessGap(string Field, double Weight, GapSeverity Severity);

/// <summary>How well-documented a record is: <c>Score</c> 0..1 (Σ weight·presence / Σ weight), the unmet
/// fields ranked by missing mass (weight·absence, largest first), and the rubric version that produced it.
/// <c>null</c> (not this type) means "not applicable".</summary>
public sealed record CompletenessScore(double Score, int RubricVersion, IReadOnlyList<CompletenessGap> Gaps);

/// <summary>
/// Pure, kind-aware completeness rubric for contacts. Scores <em>presence</em>, not quality — crude on purpose,
/// enough to rank thin-vs-rich. Organisation membership lives on a separate <see cref="ContactGroup"/> and relation
/// edges may point inward from other aggregates, so both are decided by the caller and passed in. A field acknowledged
/// as inapplicable via metadata <c>completeness.na</c> (grandma has no employer) is dropped from the rubric entirely.
/// </summary>
public static class CompletenessScorer
{
    public const int Version = 4;

    public static CompletenessScore? ScoreContact(Contact c, bool hasOrganisation, bool hasInboundRelations = false)
    {
        // An organisation/venue card (a booking provider, say) carries no person facts — name, reach, and address are the record.
        var fields = c.Kind == ContactKind.Organization
            ? new List<(string, double, double)>
            {
                ("name", 1, Name(c)),
                ("primaryReach", 3, PrimaryReach(c)),
                ("postalAddress", 2, PostalAddress(c)),
            }
            // A deceased contact needs no reach, address, or employer — remembrance data is what's worth asking for.
            : c.Deceased
                ? new List<(string, double, double)>
                {
                    ("name", 1, Name(c)),
                    ("birthday", 1, Birthday(c)),
                    ("deathDate", 1, c.DeathDate is not null ? 1 : 0),
                    ("relations", 1, Relations(c, hasInboundRelations)),
                }
                : new List<(string, double, double)>
                {
                    ("name", 1, Name(c)),
                    ("primaryReach", 3, PrimaryReach(c)),
                    ("secondaryReach", 1, DistinctMediums(c) >= 2 ? 1 : 0),
                    ("birthday", 1, Birthday(c)),
                    ("postalAddress", 1, PostalAddress(c)),
                    ("organisation", 1, hasOrganisation ? 1 : 0),
                    ("relations", 1, Relations(c, hasInboundRelations)),
                };

        var na = NaFields(c.Metadata);
        if (na.Count > 0) fields.RemoveAll(f => na.Contains(f.Item1));

        return Build(fields);
    }

    private static CompletenessScore Build(List<(string Field, double Weight, double Presence)> fields)
    {
        var totalWeight = fields.Sum(f => f.Weight);
        var score = totalWeight == 0 ? 1 : fields.Sum(f => f.Weight * f.Presence) / totalWeight;
        var gaps = fields
            .Where(f => f.Presence < 1)
            .OrderByDescending(f => f.Weight * (1 - f.Presence))
            .ThenByDescending(f => f.Weight)
            .Select(f => new CompletenessGap(f.Field, f.Weight, f.Presence == 0 ? GapSeverity.Absent : GapSeverity.Weak))
            .ToList();
        return new CompletenessScore(Math.Round(score, 4), Version, gaps);
    }

    /// <summary>Rubric fields the user acknowledged as inapplicable: metadata <c>{"completeness":{"na":["organisation"]}}</c>.</summary>
    private static HashSet<string> NaFields(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return [];
        try
        {
            if (JsonNode.Parse(metadata)?["completeness"]?["na"] is not JsonArray na) return [];
            return new HashSet<string>(
                na.Select(n => n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null).OfType<string>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return []; }
    }

    // ---- presence helpers (1 present · 0.5 weak · 0 absent) ----

    private static double Name(Contact c) =>
        !string.IsNullOrWhiteSpace(c.GivenName) || !string.IsNullOrWhiteSpace(c.FamilyName) || !string.IsNullOrWhiteSpace(c.Nickname) ? 1 : 0;

    private static double Birthday(Contact c) =>
        c.Birthday is null ? 0 : c.Birthday.Year is null ? 0.5 : 1;   // year-less month-day → the year is the ask

    private static double PrimaryReach(Contact c) =>
        c.Channels.Count > 0 ? 1 : c.Profiles.Count > 0 ? 0.5 : 0;   // a direct channel reaches; a social handle only might

    private static double PostalAddress(Contact c) =>   // only an address active today addresses a contact
        c.Addresses.Any(a => a.IsActiveOn(DateOnly.FromDateTime(DateTime.UtcNow))) ? 1 : 0;

    // Redundancy across mediums, not entries: two emails are one medium; all social profiles count as one.
    private static int DistinctMediums(Contact c) =>
        c.Channels.Select(ch => ch.Medium).Distinct().Count() + (c.Profiles.Count > 0 ? 1 : 0);

    // Own edges (ended ones still document the connection) or edges pointing inward from other contacts.
    private static double Relations(Contact c, bool inbound) => c.Relations.Count > 0 || inbound ? 1 : 0;
}
