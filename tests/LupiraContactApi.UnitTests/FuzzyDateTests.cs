using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

public class FuzzyDateTests
{
    [Theory]
    [InlineData(2015, null, null, "2015")]
    [InlineData(2015, 6, null, "2015-06")]
    [InlineData(2015, 6, 12, "2015-06-12")]
    public void Canonical_text_matches_the_stated_precision(int year, int? month, int? day, string expected) =>
        Assert.Equal(expected, new FuzzyDate(year, month, day).ToCanonical());

    [Theory]
    [InlineData(2015, null, null, true)]
    [InlineData(2015, 12, null, true)]
    [InlineData(2016, 2, 29, true)]    // leap day in a leap year
    [InlineData(2015, 13, null, false)]
    [InlineData(2015, 0, null, false)]
    [InlineData(2015, null, 12, false)] // a day without a month states nothing coherent
    [InlineData(2015, 2, 30, false)]
    [InlineData(2015, 2, 29, false)]   // not a leap year
    public void IsValid_enforces_day_requires_month_and_calendar_bounds(int year, int? month, int? day, bool valid) =>
        Assert.Equal(valid, new FuzzyDate(year, month, day).IsValid());

    [Fact]
    public void Earliest_and_latest_bound_the_stated_precision()
    {
        Assert.Equal(new DateOnly(2015, 1, 1), new FuzzyDate(2015).EarliestDate());
        Assert.Equal(new DateOnly(2015, 12, 31), new FuzzyDate(2015).LatestDate());
        Assert.Equal(new DateOnly(2015, 6, 1), new FuzzyDate(2015, 6).EarliestDate());
        Assert.Equal(new DateOnly(2015, 6, 30), new FuzzyDate(2015, 6).LatestDate());
        Assert.Equal(new DateOnly(2016, 2, 29), new FuzzyDate(2016, 2).LatestDate());
        Assert.Equal(new DateOnly(2015, 6, 12), new FuzzyDate(2015, 6, 12).LatestDate());
    }

    [Fact]
    public void Certainly_past_and_future_resolve_ambiguity_toward_neither()
    {
        var today = new DateOnly(2026, 8, 16);
        Assert.True(new FuzzyDate(2015).IsCertainlyPast(today));
        Assert.False(new FuzzyDate(2026).IsCertainlyPast(today));     // could still be ahead this year
        Assert.False(new FuzzyDate(2026).IsCertainlyFuture(today));   // could already have happened
        Assert.True(new FuzzyDate(2027).IsCertainlyFuture(today));
        Assert.True(new FuzzyDate(2026, 12).IsCertainlyFuture(today));
        Assert.True(new FuzzyDate(2026, 7).IsCertainlyPast(today));
        Assert.False(new FuzzyDate(2026, 8).IsCertainlyPast(today));
        Assert.False(new FuzzyDate(2026, 8).IsCertainlyFuture(today));
    }

    [Fact]
    public void DefinitelyAfter_compares_at_the_coarsest_shared_precision()
    {
        Assert.True(FuzzyDate.DefinitelyAfter(new(2016), new(2015)));
        Assert.False(FuzzyDate.DefinitelyAfter(new(2015), new(2016)));
        Assert.False(FuzzyDate.DefinitelyAfter(new(2015, 6), new(2015)));      // "2015-06" is not certainly after "2015"
        Assert.False(FuzzyDate.DefinitelyAfter(new(2015), new(2015)));         // same year, compatible
        Assert.True(FuzzyDate.DefinitelyAfter(new(2015, 7), new(2015, 6)));
        Assert.False(FuzzyDate.DefinitelyAfter(new(2015, 6, 20), new(2015, 6))); // day vs month-only: compatible
        Assert.True(FuzzyDate.DefinitelyAfter(new(2015, 6, 20), new(2015, 6, 12)));
        Assert.False(FuzzyDate.DefinitelyAfter(new(2015, 6, 12), new(2015, 6, 12)));
    }
}
