namespace LupiraContactApi.Domain;

/// <summary>Canonicalizes reach channels: trim the value, lowercase the type token (blank → null), drop empty-valued
/// entries, and dedupe by (Medium, Value) case-insensitively (first wins). The ≤1-preferred-per-medium rule is the
/// caller's to enforce (a hard error on explicit sets; conflict-avoiding on merge).</summary>
public static class ReachChannelNormalizer
{
    public static IReadOnlyList<ContactReachChannel> Normalize(IEnumerable<ContactReachChannel> channels) =>
        channels
            .Select(c => c with { Value = c.Value.Trim(), Type = string.IsNullOrWhiteSpace(c.Type) ? null : c.Type.Trim().ToLowerInvariant() })
            .Where(c => c.Value.Length > 0)
            .DistinctBy(c => (c.Medium, Value: c.Value.ToLowerInvariant()))
            .ToList();

    /// <summary>True when any medium carries more than one preferred channel.</summary>
    public static bool HasPreferredConflict(IEnumerable<ContactReachChannel> channels) =>
        channels.GroupBy(c => c.Medium).Any(g => g.Count(c => c.Preferred) > 1);
}
