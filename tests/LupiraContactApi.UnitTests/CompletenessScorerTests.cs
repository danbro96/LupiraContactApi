using LupiraContactApi.Core.Domain.Completeness;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Rubric v3: kind-aware (person/organisation/deceased cuts), weak states for year-less birthdays,
/// profile-only reach and unresolved addresses, relations scored, N/A acknowledgments, deficit-ranked gaps.</summary>
public class CompletenessScorerTests
{
    private static Contact Person(bool deceased = false, DateOnly? deathDate = null) => new()
    {
        Id = Guid.NewGuid(),
        GivenName = "Jane",
        FamilyName = "Smith",
        Birthday = new PartialDate(1950, 5, 5),
        Deceased = deceased,
        DeathDate = deathDate,
    };

    private static ContactRelation Edge() => new() { ToContactId = Guid.NewGuid(), Kind = ContactRelationKind.Child };

    [Fact]
    public void Version_is_4() => Assert.Equal(4, CompletenessScorer.Version);

    [Fact]
    public void Living_contact_is_penalized_for_missing_reach()
    {
        var s = CompletenessScorer.ScoreContact(Person(), hasOrganisation: false)!;
        Assert.Contains(s.Gaps, g => g.Field == "primaryReach");
        Assert.Contains(s.Gaps, g => g.Field == "postalAddress");
        Assert.Contains(s.Gaps, g => g.Field == "organisation");
        Assert.Contains(s.Gaps, g => g.Field == "relations");
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
    public void Deceased_contact_with_name_birthday_deathdate_and_kin_is_complete()
    {
        var c = Person(deceased: true, deathDate: new DateOnly(2020, 3, 14));
        c.Relations = [Edge()];
        var s = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;
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
    public void Profile_only_reach_is_weak_not_full()
    {
        var c = Person();
        c.Profiles = [new ContactSocialProfile { Service = "telegram", Handle = "jane" }];
        var s = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;
        Assert.Equal(GapSeverity.Weak, s.Gaps.Single(g => g.Field == "primaryReach").Severity);
    }

    [Fact]
    public void Secondary_reach_needs_a_second_medium_not_a_second_entry()
    {
        var c = Person();
        c.Channels =
        [
            new ContactReachChannel(ReachMedium.Email, "jane@x.test", null, false),
            new ContactReachChannel(ReachMedium.Email, "jane2@x.test", null, false),
        ];
        Assert.Contains(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "secondaryReach");

        c.Channels = [.. c.Channels, new ContactReachChannel(ReachMedium.Phone, "+46123", null, false)];
        Assert.DoesNotContain(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "secondaryReach");
    }

    [Fact]
    public void Yearless_birthday_is_weak()
    {
        var c = Person();
        c.Birthday = new PartialDate(null, 5, 5);
        var s = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;
        Assert.Equal(GapSeverity.Weak, s.Gaps.Single(g => g.Field == "birthday").Severity);
    }

    [Fact]
    public void Former_only_address_scores_postal_zero()
    {
        var c = Person();
        c.Addresses = [new ContactPostalAddress { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home, MovedOut = new FuzzyDate(2015) }];
        Assert.Equal(GapSeverity.Absent, CompletenessScorer.ScoreContact(c, false)!.Gaps.Single(g => g.Field == "postalAddress").Severity);
    }

    [Fact]
    public void Current_address_scores_postal_full()
    {
        var c = Person();
        c.Addresses = [new ContactPostalAddress { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home }];
        Assert.DoesNotContain(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "postalAddress");
    }

    [Fact]
    public void Future_move_out_is_still_an_address_future_move_in_is_not()
    {
        var c = Person();
        c.Addresses = [new ContactPostalAddress { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home, MovedOut = new FuzzyDate(9999) }];
        Assert.DoesNotContain(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "postalAddress");

        c.Addresses = [new ContactPostalAddress { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home, MovedIn = new FuzzyDate(9999) }];
        Assert.Contains(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "postalAddress");
    }

    [Fact]
    public void Relations_credit_own_or_inbound_edges()
    {
        var c = Person();
        Assert.Contains(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "relations");

        Assert.DoesNotContain(CompletenessScorer.ScoreContact(c, false, hasInboundRelations: true)!.Gaps, g => g.Field == "relations");

        c.Relations = [Edge()];
        Assert.DoesNotContain(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "relations");
    }

    [Fact]
    public void Organisation_card_scores_name_reach_address_only()
    {
        var venue = new Contact { Id = Guid.NewGuid(), Kind = ContactKind.Organization, GivenName = "Trattoria Nonna" };
        var s = CompletenessScorer.ScoreContact(venue, hasOrganisation: false)!;

        Assert.DoesNotContain(s.Gaps, g => g.Field is "birthday" or "organisation" or "secondaryReach" or "relations");
        Assert.Contains(s.Gaps, g => g.Field == "primaryReach");
        Assert.Contains(s.Gaps, g => g.Field == "postalAddress");
    }

    [Fact]
    public void Acknowledged_na_fields_are_dropped_from_the_rubric()
    {
        var c = Person();
        c.Channels = [new ContactReachChannel(ReachMedium.Phone, "+46123", null, false)];
        c.Relations = [Edge()];
        c.Addresses = [new ContactPostalAddress { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home }];
        var before = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;
        Assert.Contains(before.Gaps, g => g.Field is "organisation" or "secondaryReach");

        c.Metadata = """{"completeness":{"na":["organisation","secondaryReach"]}}""";
        var after = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;
        Assert.Equal(1, after.Score);
        Assert.Empty(after.Gaps);
    }

    [Fact]
    public void Unknown_na_names_and_malformed_metadata_are_ignored()
    {
        var c = Person();
        c.Metadata = """{"completeness":{"na":["nonsense", 42]}}""";
        Assert.Contains(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "primaryReach");

        c.Metadata = "not json {";
        Assert.Contains(CompletenessScorer.ScoreContact(c, false)!.Gaps, g => g.Field == "primaryReach");
    }

    [Fact]
    public void Gaps_rank_by_missing_mass_not_raw_weight()
    {
        // primaryReach weak (deficit 3·0.5 = 1.5) still outranks the weight-1 absents; among those,
        // an absent (1.0) outranks the weak birthday (0.5).
        var c = Person();
        c.Birthday = new PartialDate(null, 5, 5);
        c.Profiles = [new ContactSocialProfile { Service = "telegram", Handle = "jane" }];
        var s = CompletenessScorer.ScoreContact(c, hasOrganisation: false)!;

        Assert.Equal("primaryReach", s.Gaps[0].Field);
        Assert.Equal("birthday", s.Gaps[^1].Field);
    }
}
