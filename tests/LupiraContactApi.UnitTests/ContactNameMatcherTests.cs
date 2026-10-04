using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.UnitTests;

public class ContactNameMatcherTests
{
    private static Contact Person(string? given, string? family, string? middle = null, string? nickname = null, DisplayNameFormat format = DisplayNameFormat.Full) =>
        new() { Id = Guid.NewGuid(), GivenName = given, MiddleName = middle, FamilyName = family, Nickname = nickname, DisplayNameFormat = format };

    private static ContactNameMatch ResolveOne(string name, params Contact[] pool) => ContactNameMatcher.Resolve([name], pool).Single();

    [Theory]
    [InlineData("Irène", "irene")]
    [InlineData("  Anna-Karin   O'Brien ", "anna karin o brien")]
    [InlineData("Søren Ærø", "soren aero")]
    [InlineData("Strauß", "strauss")]
    [InlineData("ÅSA ÖBERG", "asa oberg")]
    public void Fold_drops_case_diacritics_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, ContactNameMatcher.Fold(input));

    [Fact]
    public void Diacritics_in_the_contact_match_a_plain_query()
    {
        var irene = Person("Irène", "Modig");

        var match = ResolveOne("Irene Modig", irene, Person("Irma", "Modig"));

        Assert.Equal(NameMatchOutcome.Matched, match.Outcome);
        Assert.Equal(irene.Id, match.ContactId);
    }

    [Fact]
    public void Given_and_family_match_past_middle_names()
    {
        var anton = Person("Anton", "Alfonsson", middle: "Karl Erik");

        var match = ResolveOne("Anton Alfonsson", anton, Person("Anna", "Alfonsson"));

        Assert.Equal(NameMatchOutcome.Matched, match.Outcome);
        Assert.Equal(anton.Id, match.ContactId);
    }

    [Fact]
    public void Multi_word_family_name_matches_with_or_without_hyphen()
    {
        var lisa = Person("Lisa", "Winberg Nielsen");

        Assert.Equal(lisa.Id, ResolveOne("Lisa Winberg-Nielsen", lisa).ContactId);
        Assert.Equal(lisa.Id, ResolveOne("Winberg Nielsen", lisa, Person("Lisa", "Winberg")).ContactId);
    }

    [Fact]
    public void Nickname_only_contact_matches_its_nickname()
    {
        var raen = Person(null, null, nickname: "Raen", format: DisplayNameFormat.NickName);

        var match = ResolveOne("raen", raen, Person("Rae", "Nilsson"));

        Assert.Equal(NameMatchOutcome.Matched, match.Outcome);
        Assert.Equal(raen.Id, match.ContactId);
    }

    [Fact]
    public void Nickname_plus_family_is_a_full_form()
    {
        var bosse = Person("Bo", "Ek", nickname: "Bosse");

        Assert.Equal(bosse.Id, ResolveOne("Bosse Ek", bosse, Person("Bo", "Ek")).ContactId);
    }

    [Fact]
    public void Padded_name_parts_still_match()
    {
        var anna = Person("Anna", " Weideskog");

        var match = ResolveOne("Anna Weideskog", anna);

        Assert.Equal(NameMatchOutcome.Matched, match.Outcome);
        Assert.Equal(anna.Id, match.ContactId);
    }

    [Fact]
    public void Nordic_letters_match_their_ascii_spelling()
    {
        var soren = Person("Søren", "Ærø");

        Assert.Equal(soren.Id, ResolveOne("Soren Aero", soren).ContactId);
    }

    [Fact]
    public void Lone_token_hit_on_a_single_word_query_stays_ambiguous()
    {
        var anna = Person("Anna", "Svensson");

        var match = ResolveOne("Svensson", anna);

        Assert.Equal(NameMatchOutcome.Ambiguous, match.Outcome);
        Assert.Null(match.ContactId);
        Assert.Equal(anna.Id, Assert.Single(match.Candidates).ContactId);
    }

    [Fact]
    public void Lone_token_hit_on_a_multi_word_query_matches()
    {
        var anna = Person("Anna", "Svensson", middle: "Maria");

        var match = ResolveOne("Maria Svensson", anna, Person("Maria", "Berg"));

        Assert.Equal(NameMatchOutcome.Matched, match.Outcome);
        Assert.Equal(anna.Id, match.ContactId);
    }

    [Fact]
    public void Several_full_form_hits_are_ambiguous_over_only_those()
    {
        var first = Person("Erik", "Lund");
        var second = Person("Erik", "Lund");

        var match = ResolveOne("Erik Lund", first, second, Person("Erik", "Lund", middle: "Johan", format: DisplayNameFormat.FirstLast), Person("Lund", "Erik"));

        Assert.Equal(NameMatchOutcome.Ambiguous, match.Outcome);
        Assert.Equal(3, match.Candidates.Count);
    }

    [Fact]
    public void Full_form_hit_beats_token_hits()
    {
        var erik = Person("Erik", "Lund");

        var match = ResolveOne("Erik Lund", erik, Person("Lund", "Erik"));

        Assert.Equal(NameMatchOutcome.Matched, match.Outcome);
        Assert.Equal(erik.Id, match.ContactId);
    }

    [Fact]
    public void Several_token_hits_are_ambiguous_and_capped()
    {
        var pool = Enumerable.Range(0, 8).Select(i => Person($"Given{i}", "Berg")).ToArray();

        var match = ResolveOne("Berg", pool);

        Assert.Equal(NameMatchOutcome.Ambiguous, match.Outcome);
        Assert.Equal(ContactNameMatcher.MaxCandidates, match.Candidates.Count);
    }

    [Theory]
    [InlineData("Nobody Here")]
    [InlineData("  ")]
    [InlineData("!!")]
    public void No_token_hit_is_not_found(string name)
    {
        var match = ResolveOne(name, Person("Anna", "Svensson"));

        Assert.Equal(NameMatchOutcome.NotFound, match.Outcome);
        Assert.Empty(match.Candidates);
    }

    [Theory]
    [InlineData("Rosen")]
    [InlineData("ros")]
    [InlineData("Rosén Karin")]
    [InlineData("Kar")]
    public void Search_matches_folded_tokens_or_prefixes(string query)
    {
        var karin = Person("Karin", "Rosén");

        Assert.Same(karin, Assert.Single(ContactNameMatcher.Search([karin, Person("Per", "Lind")], query)));
    }

    [Fact]
    public void Search_with_only_punctuation_finds_nothing() =>
        Assert.Empty(ContactNameMatcher.Search([Person("Anna", "Svensson")], "--"));
}
