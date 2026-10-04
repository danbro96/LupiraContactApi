using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Residencies.Events;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The residency aggregate: currency over fuzzy periods, and decisions that append nothing when the stated state holds.</summary>
public class ResidencyTests
{
    private static readonly DateOnly Today = new(2026, 8, 16);

    private static Residency Lived(FuzzyDate? movedIn = null, FuzzyDate? movedOut = null) =>
        new() { Id = Guid.NewGuid(), ContactId = Guid.NewGuid(), PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home, MovedIn = movedIn, MovedOut = movedOut };

    [Fact]
    public void Current_means_today_falls_inside_the_period_with_ambiguity_toward_active()
    {
        Assert.True(Lived().IsActiveOn(Today));
        Assert.False(Lived(movedOut: new FuzzyDate(2015)).IsActiveOn(Today));
        Assert.True(Lived(movedOut: new FuzzyDate(2027, 3)).IsActiveOn(Today));    // a planned move-out
        Assert.False(Lived(movedIn: new FuzzyDate(2027)).IsActiveOn(Today));      // not moved in yet
        Assert.True(Lived(movedOut: new FuzzyDate(2026)).IsActiveOn(Today));      // "sometime this year" might still be ahead
    }

    [Fact]
    public void Restating_what_holds_appends_nothing()
    {
        var r = Lived(new FuzzyDate(2010), new FuzzyDate(2015, 6));
        Assert.Empty(r.Revise(r.PlaceId, r.Type, r.Label, new FuzzyDate(2010), new FuzzyDate(2015, 6)));
        Assert.Single(r.Revise(r.PlaceId, ContactAddressType.Vacation, "Summer house", r.MovedIn, r.MovedOut));
        Assert.Empty(r.MoveOut(new FuzzyDate(2015, 6)));
        Assert.IsType<ResidencyMovedOut>(Assert.Single(r.MoveOut(new FuzzyDate(2016))));
    }

    [Fact]
    public void Starting_one_names_its_contact_and_place()
    {
        var (id, contact, place) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var started = Assert.IsType<ResidencyStarted>(Assert.Single(Residency.Start(id, contact, place, ContactAddressType.Vacation, "Gotland", new FuzzyDate(2020), null)));
        Assert.Equal((id, contact, place, ContactAddressType.Vacation, "Gotland"), (started.ResidencyId, started.ContactId, started.PlaceId, started.Type, started.Label));
    }
}
