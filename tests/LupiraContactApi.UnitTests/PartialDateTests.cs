using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The year-optional date value object: canonical text, both parse forms, and DateOnly interop.</summary>
public class PartialDateTests
{
    [Fact]
    public void Full_date_canonicalizes_with_year() =>
        Assert.Equal("1996-05-15", new PartialDate(1996, 5, 15).ToCanonical());

    [Fact]
    public void Year_less_date_canonicalizes_without_year() =>
        Assert.Equal("--06-17", new PartialDate(null, 6, 17).ToCanonical());

    [Theory]
    [InlineData("1996-05-15", 1996, 5, 15)]
    [InlineData("19960515", 1996, 5, 15)]
    public void Parses_full_dates(string s, int y, int m, int d) =>
        Assert.Equal(new PartialDate(y, m, d), PartialDate.Parse(s));

    [Theory]
    [InlineData("--06-17", 6, 17)]
    [InlineData("--0617", 6, 17)]
    public void Parses_year_less_dates(string s, int m, int d) =>
        Assert.Equal(new PartialDate(null, m, d), PartialDate.Parse(s));

    [Fact]
    public void Year_less_leap_day_is_valid() =>
        Assert.Equal(new PartialDate(null, 2, 29), PartialDate.Parse("--0229"));

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("--1345")]   // month 13
    public void Rejects_unparseable(string s) => Assert.Null(PartialDate.Parse(s));

    [Fact]
    public void ToDateOnly_is_null_without_a_year()
    {
        Assert.Null(new PartialDate(null, 6, 17).ToDateOnly());
        Assert.Equal(new DateOnly(1996, 5, 15), new PartialDate(1996, 5, 15).ToDateOnly());
    }
}
