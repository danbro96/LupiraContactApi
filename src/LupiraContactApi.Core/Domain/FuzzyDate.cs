namespace LupiraContactApi.Domain;

/// <summary>A date known to year, year-month, or full-day precision — the precision itself carries the certainty
/// ("2015" means "sometime in 2015"). Used for residency boundaries on <see cref="ContactPostalAddress"/>.
/// <see cref="Year"/> is always present; <see cref="Day"/> requires <see cref="Month"/>. Distinct from
/// <see cref="PartialDate"/>, which models the opposite case (year unknown, month-day known).</summary>
public sealed record FuzzyDate(int Year, int? Month = null, int? Day = null)
{
    /// <summary>Deterministic text at the stated precision: <c>2015</c>, <c>2015-06</c>, or <c>2015-06-12</c>.</summary>
    public string ToCanonical() => (Month, Day) switch
    {
        (null, _) => $"{Year:D4}",
        ({ } m, null) => $"{Year:D4}-{m:D2}",
        ({ } m, { } d) => $"{Year:D4}-{m:D2}-{d:D2}",
    };

    public bool IsValid() => (Month, Day) switch
    {
        (null, null) => true,
        (null, not null) => false, // a day without a month states nothing coherent
        ({ } m, null) => m is >= 1 and <= 12,
        ({ } m, { } d) => m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(Year, m),
    };

    /// <summary>True only when <paramref name="a"/> is <b>certainly</b> after <paramref name="b"/> — compared at the
    /// coarsest precision both state. An absent component keeps the pair compatible ("2015-06" is not after "2015"),
    /// so only definite violations reject.</summary>
    public static bool DefinitelyAfter(FuzzyDate a, FuzzyDate b)
    {
        if (a.Year != b.Year) return a.Year > b.Year;
        if (a.Month is not { } am || b.Month is not { } bm) return false;
        if (am != bm) return am > bm;
        if (a.Day is not { } ad || b.Day is not { } bd) return false;
        return ad > bd;
    }
}
