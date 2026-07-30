using System.Globalization;

namespace LupiraContactApi.Domain;

/// <summary>A calendar date that may omit the year — a birthday is often known only as a month-day.
/// <see cref="Month"/> and <see cref="Day"/> are always present; <see cref="Year"/> is null when unknown.
/// Canonical text is <c>yyyy-MM-dd</c> with a year, else <c>--MM-dd</c> (a serialization concern; wire formats
/// live at the seam, see <see cref="Serialization.VCardSerializer"/>).</summary>
public sealed record PartialDate(int? Year, int Month, int Day)
{
    /// <summary>The full date when the year is known, else null.</summary>
    public DateOnly? ToDateOnly() => Year is { } y ? new DateOnly(y, Month, Day) : null;

    public static PartialDate FromDate(DateOnly d) => new(d.Year, d.Month, d.Day);

    /// <summary>Deterministic text for hashing: <c>yyyy-MM-dd</c> with a year, else <c>--MM-dd</c>.</summary>
    public string ToCanonical() => Year is { } y
        ? $"{y:D4}-{Month:D2}-{Day:D2}"
        : $"--{Month:D2}-{Day:D2}";

    /// <summary>Parses <c>yyyy-MM-dd</c>, <c>yyyyMMdd</c>, <c>--MM-dd</c>, or <c>--MMdd</c> (year-less). Null when unparseable.</summary>
    public static PartialDate? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        if (s.StartsWith("--", StringComparison.Ordinal))
        {
            var md = s[2..].Replace("-", "");
            return md.Length == 4
                && int.TryParse(md[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var mm)
                && int.TryParse(md[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var dd)
                && IsValid(null, mm, dd)
                    ? new PartialDate(null, mm, dd) : null;
        }

        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return FromDate(d1);
        if (DateOnly.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return FromDate(d2);
        return null;
    }

    // Feb 29 is allowed year-less (a leap year is used as the reference).
    private static bool IsValid(int? year, int month, int day) =>
        month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year ?? 2000, month);
}
