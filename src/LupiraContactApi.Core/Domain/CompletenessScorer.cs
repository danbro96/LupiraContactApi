using System.Text.Json.Serialization;

namespace LupiraContactApi.Domain;

/// <summary>A field is fully present, weak/partial (0.5), or absent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GapSeverity>))]
public enum GapSeverity { Weak, Absent }

/// <summary>A field the record is missing or thin on, with its rubric weight (heavier = ask first).</summary>
public sealed record CompletenessGap(string Field, double Weight, GapSeverity Severity);

/// <summary>How well-documented a record is: <c>Score</c> 0..1 (Σ weight·presence / Σ weight), the unmet
/// fields ranked heaviest-first, and the rubric version that produced it. <c>null</c> (not this type) means "not applicable".</summary>
public sealed record CompletenessScore(double Score, int RubricVersion, IReadOnlyList<CompletenessGap> Gaps);

/// <summary>
/// Pure completeness rubric for contacts. Scores <em>presence</em>, not quality — crude on purpose,
/// enough to rank thin-vs-rich. Organisation membership lives on a separate <see cref="ContactGroup"/>,
/// so it is decided by the caller and passed in.
/// </summary>
public static class CompletenessScorer
{
    public const int Version = 2;

    public static CompletenessScore? ScoreContact(Contact c, bool hasOrganisation)
    {
        // A deceased contact needs no reach, address, or employer — remembrance data is what's worth asking for.
        var fields = c.Deceased
            ? new List<(string, double, double)>
            {
                ("name", 1, Name(c)),
                ("birthday", 1, c.Birthday is not null ? 1 : 0),
                ("deathDate", 1, c.DeathDate is not null ? 1 : 0),
            }
            : new List<(string, double, double)>
            {
                ("name", 1, Name(c)),
                ("primaryReach", 3, AnyReach(c) ? 1 : 0),
                ("secondaryReach", 1, ReachCount(c) >= 2 ? 1 : 0),
                ("birthday", 1, c.Birthday is not null ? 1 : 0),
                ("postalAddress", 1, c.Addresses.Count > 0 ? 1 : 0),
                ("organisation", 1, hasOrganisation ? 1 : 0),
            };
        return Build(fields);
    }

    private static CompletenessScore Build(List<(string Field, double Weight, double Presence)> fields)
    {
        var totalWeight = fields.Sum(f => f.Weight);
        var score = totalWeight == 0 ? 1 : fields.Sum(f => f.Weight * f.Presence) / totalWeight;
        var gaps = fields
            .Where(f => f.Presence < 1)
            .OrderByDescending(f => f.Weight)
            .Select(f => new CompletenessGap(f.Field, f.Weight, f.Presence == 0 ? GapSeverity.Absent : GapSeverity.Weak))
            .ToList();
        return new CompletenessScore(Math.Round(score, 4), Version, gaps);
    }

    private static double Name(Contact c) =>
        !string.IsNullOrWhiteSpace(c.GivenName) || !string.IsNullOrWhiteSpace(c.FamilyName) || !string.IsNullOrWhiteSpace(c.Nickname) ? 1 : 0;

    private static bool AnyReach(Contact c) => ReachCount(c) >= 1;
    private static int ReachCount(Contact c) => c.Channels.Count + c.Profiles.Count;
}
