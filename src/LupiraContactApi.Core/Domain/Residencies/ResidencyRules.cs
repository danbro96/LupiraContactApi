using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Residencies;

/// <summary>What a residency must satisfy, alone and beside the contact's other live residencies. Fuzzy dates only reject
/// what is certainly wrong: "moved out 2019, moved back in 2019" is two residencies that may well not overlap.</summary>
public static class ResidencyRules
{
    /// <summary>The refusal, or null when the period is valid and overlaps none of <paramref name="others"/> at the same place.</summary>
    public static string? Check(Guid placeId, FuzzyDate? movedIn, FuzzyDate? movedOut, IEnumerable<Residency> others)
    {
        if (placeId == Guid.Empty) return "Each residency must reference a geo place id.";
        if ((movedIn is { } mi && !mi.IsValid()) || (movedOut is { } mo && !mo.IsValid()))
            return "Residency dates must be a valid year, year-month, or year-month-day.";
        if (movedIn is { } a && movedOut is { } b && FuzzyDate.DefinitelyAfter(a, b))
            return "Moved-in must not be after moved-out.";
        return others.Any(o => o.IsLive && o.PlaceId == placeId && !MaySeparate(movedIn, movedOut, o.MovedIn, o.MovedOut))
            ? "The contact already lives there during that period."
            : null;
    }

    // Two periods may not overlap when one can end no later than the other starts. An open end never ends.
    private static bool MaySeparate(FuzzyDate? aIn, FuzzyDate? aOut, FuzzyDate? bIn, FuzzyDate? bOut) =>
        (aOut is { } ao && bIn is { } bi && !FuzzyDate.DefinitelyAfter(ao, bi)) ||
        (bOut is { } bo && aIn is { } ai && !FuzzyDate.DefinitelyAfter(bo, ai));
}
