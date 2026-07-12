using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The canonical domain text behind <c>ContentHash</c>: deterministic, sensitive to every content-bearing
/// dimension (including order), and deliberately blind to addresses.</summary>
public class ContactContentTests
{
    static readonly ContactFields Fields = new("Jane", null, "Smith", null,
        [new ContactReachChannel(ReachMedium.Email, "j@x.com", null, false)], new PartialDate(1990, 2, 15), null);

    static string Canonical(
        ContactFields? f = null,
        IReadOnlyList<ContactRelation>? relations = null,
        IReadOnlyList<Guid>? emergency = null,
        IReadOnlyList<ContactSocialProfile>? profiles = null,
        bool deceased = false, DateOnly? deathDate = null) =>
        ContactContent.Canonical("uid@x", f ?? Fields, relations ?? [], emergency ?? [], profiles ?? [], deceased, deathDate);

    [Fact]
    public void Identical_input_yields_identical_text() =>
        Assert.Equal(Canonical(), Canonical());

    [Fact]
    public void Every_content_bearing_dimension_changes_the_text()
    {
        var baseline = Canonical();
        var other = Guid.NewGuid();

        Assert.NotEqual(baseline, Canonical(f: Fields with { GivenName = "Janet" }));
        Assert.NotEqual(baseline, Canonical(f: Fields with { Channels = [new ContactReachChannel(ReachMedium.Email, "j@x.com", null, false), new ContactReachChannel(ReachMedium.Email, "extra@x.com", null, false)] }));
        Assert.NotEqual(baseline, Canonical(f: Fields with { Channels = [new ContactReachChannel(ReachMedium.Email, "j@x.com", "work", false)] }));   // type is content-bearing
        Assert.NotEqual(baseline, Canonical(f: Fields with { Channels = [new ContactReachChannel(ReachMedium.Email, "j@x.com", null, true)] }));     // preferred is content-bearing
        Assert.NotEqual(baseline, Canonical(relations: [new ContactRelation { ToContactId = other, Kind = ContactRelationKind.Friend }]));
        Assert.NotEqual(baseline, Canonical(emergency: [other]));
        Assert.NotEqual(baseline, Canonical(profiles: [new ContactSocialProfile { Service = "telegram", Handle = "j" }]));
        Assert.NotEqual(baseline, Canonical(deceased: true));
        Assert.NotEqual(Canonical(deceased: true), Canonical(deceased: true, deathDate: new DateOnly(2020, 1, 1)));
        Assert.NotEqual(baseline, Canonical(f: Fields with { Notes = "met at KTH" }));
        Assert.NotEqual(baseline, Canonical(f: Fields with { Pronouns = "they/them" }));
        Assert.NotEqual(baseline, Canonical(f: Fields with { Birthday = new PartialDate(null, 2, 15) }));   // dropping the year is a real change
    }

    [Fact]
    public void DisplayNameFormat_is_rendering_only_and_does_not_change_the_text()
    {
        var baseline = Canonical();
        Assert.Equal(baseline, Canonical(f: Fields with { DisplayNameFormat = DisplayNameFormat.FirstLast }));
        Assert.Equal(baseline, Canonical(f: Fields with { DisplayNameFormat = DisplayNameFormat.NickName }));
    }

    [Fact]
    public void Relation_since_and_note_are_content_bearing()
    {
        var other = Guid.NewGuid();
        ContactRelation Edge(DateOnly? since = null, string? note = null) => new() { ToContactId = other, Kind = ContactRelationKind.Friend, Since = since, Note = note };
        var bare = Canonical(relations: [Edge()]);
        Assert.NotEqual(bare, Canonical(relations: [Edge(since: new DateOnly(2016, 1, 1))]));
        Assert.NotEqual(bare, Canonical(relations: [Edge(note: "sailing")]));
    }

    [Fact]
    public void Ended_and_until_are_content_bearing()
    {
        var other = Guid.NewGuid();
        ContactRelation Edge(bool ended, DateOnly? until = null) => new() { ToContactId = other, Kind = ContactRelationKind.Spouse, Ended = ended, Until = until };
        var live = Canonical(relations: [Edge(false)]);
        var ended = Canonical(relations: [Edge(true)]);
        var dated = Canonical(relations: [Edge(true, new DateOnly(2024, 6, 1))]);
        Assert.NotEqual(live, ended);
        Assert.NotEqual(ended, dated);
    }

    [Fact]
    public void Preferred_flag_and_ordering_are_content_bearing()
    {
        ContactSocialProfile Tg(bool pref) => new() { Service = "telegram", Handle = "j", Preferred = pref };
        var wa = new ContactSocialProfile { Service = "whatsapp", Handle = "j" };
        Assert.NotEqual(Canonical(profiles: [Tg(false)]), Canonical(profiles: [Tg(true)]));
        Assert.NotEqual(Canonical(profiles: [Tg(false), wa]), Canonical(profiles: [wa, Tg(false)]));

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.NotEqual(Canonical(emergency: [a, b]), Canonical(emergency: [b, a]));   // order = priority
    }

    [Fact]
    public void Separator_characters_in_values_cannot_forge_another_line()
    {
        var sneaky = Canonical(f: Fields with { GivenName = "Jane|Smith\nemail|x@y" });
        var honest = ContactContent.Canonical("uid@x", Fields with { GivenName = "Jane" }, [], [], [], false, null);
        Assert.NotEqual(honest, sneaky);
    }
}
