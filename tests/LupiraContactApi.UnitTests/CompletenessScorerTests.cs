using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Rubric v2: living contacts score reach/address/organisation; the deceased score remembrance data only.</summary>
public class CompletenessScorerTests
{
    static Contact Person(bool deceased = false, DateOnly? deathDate = null) => new()
    {
        Id = Guid.NewGuid(),
        GivenName = "Jane",
        FamilyName = "Smith",
        Birthday = new DateOnly(1950, 5, 5),
        Deceased = deceased,
        DeathDate = deathDate,
    };

    [Fact]
    public void Version_is_2() => Assert.Equal(2, CompletenessScorer.Version);

    [Fact]
    public void Living_contact_is_penalized_for_missing_reach()
    {
        var s = CompletenessScorer.ScoreContact(Person(), hasOrganisation: false)!;
        Assert.Contains(s.Gaps, g => g.Field == "primaryReach");
        Assert.Contains(s.Gaps, g => g.Field == "postalAddress");
        Assert.Contains(s.Gaps, g => g.Field == "organisation");
        Assert.True(s.Score < 1);
    }

    [Fact]
    public void Deceased_contact_is_not_asked_for_reach_address_or_employer()
    {
        var s = CompletenessScorer.ScoreContact(Person(deceased: true), hasOrganisation: false)!;
        Assert.DoesNotContain(s.Gaps, g => g.Field is "primaryReach" or "secondaryReach" or "postalAddress" or "organisation");
        Assert.Contains(s.Gaps, g => g.Field == "deathDate");   // the enrichment worth prompting for
    }

    [Fact]
    public void Deceased_contact_with_name_birthday_and_deathdate_is_complete()
    {
        var s = CompletenessScorer.ScoreContact(Person(deceased: true, deathDate: new DateOnly(2020, 3, 14)), hasOrganisation: false)!;
        Assert.Equal(1, s.Score);
        Assert.Empty(s.Gaps);
    }

    [Fact]
    public void Living_contact_with_a_phone_gains_primary_reach()
    {
        var c = Person();
        c.Channels = [new ContactReachChannel(ReachMedium.Phone, "+46123", null, false)];
        var s = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;
        Assert.DoesNotContain(s.Gaps, g => g.Field == "primaryReach");
        Assert.Contains(s.Gaps, g => g.Field == "secondaryReach");
    }

    [Fact]
    public void A_social_profile_counts_as_reach()
    {
        var c = Person();
        c.Profiles = [new ContactSocialProfile { Service = "telegram", Handle = "jane" }];
        var s = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;
        Assert.DoesNotContain(s.Gaps, g => g.Field == "primaryReach");
    }
}
