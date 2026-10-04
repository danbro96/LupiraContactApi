using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>What a residency must satisfy: valid fuzzy dates in order, and no certain overlap with another residency at the same place.</summary>
public class ResidencyRulesTests
{
    private static readonly Guid Place = Guid.NewGuid();

    private static Residency At(Guid place, FuzzyDate? movedIn, FuzzyDate? movedOut) =>
        new() { Id = Guid.NewGuid(), PlaceId = place, Type = ContactAddressType.Home, MovedIn = movedIn, MovedOut = movedOut };

    [Fact]
    public void Rejects_invalid_or_inverted_dates_and_a_missing_place()
    {
        Assert.NotNull(ResidencyRules.Check(Guid.Empty, null, null, []));
        Assert.NotNull(ResidencyRules.Check(Place, new FuzzyDate(2020, 13), null, []));
        Assert.NotNull(ResidencyRules.Check(Place, new FuzzyDate(2020), new FuzzyDate(2019), []));
        Assert.Null(ResidencyRules.Check(Place, new FuzzyDate(2020), new FuzzyDate(2020, 6), []));   // compatible precisions
    }

    [Fact]
    public void Rejects_a_certain_overlap_at_the_same_place_only()
    {
        Residency[] lived = [At(Place, new FuzzyDate(2010), new FuzzyDate(2015))];

        Assert.NotNull(ResidencyRules.Check(Place, new FuzzyDate(2012), null, lived));
        Assert.NotNull(ResidencyRules.Check(Place, null, null, lived));
        Assert.Null(ResidencyRules.Check(Guid.NewGuid(), new FuzzyDate(2012), null, lived));   // another place is fine
    }

    [Fact]
    public void Moving_back_the_same_year_may_not_overlap()
    {
        Residency[] lived = [At(Place, new FuzzyDate(2010), new FuzzyDate(2019))];
        Assert.Null(ResidencyRules.Check(Place, new FuzzyDate(2019), null, lived));
        Assert.Null(ResidencyRules.Check(Place, new FuzzyDate(2021), null, lived));
    }

    [Fact]
    public void An_open_residency_blocks_a_later_open_one_at_the_same_place()
    {
        Residency[] lived = [At(Place, new FuzzyDate(2018), null)];
        Assert.NotNull(ResidencyRules.Check(Place, new FuzzyDate(2022), null, lived));
        Assert.Null(ResidencyRules.Check(Place, new FuzzyDate(2010), new FuzzyDate(2017), lived));   // an earlier, closed stay
    }
}
