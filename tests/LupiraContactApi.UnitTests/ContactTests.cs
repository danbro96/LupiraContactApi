using JasperFx.Events;
using Lupira.Identity.Marten;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Event-replay behavior of the <see cref="Contact"/> aggregate: composed display name, soft-delete +
/// resurrection, wholesale replacement of addresses/profiles/relations, derived ContentHash, and metadata attribution.</summary>
public class ContactTests
{
    private const string Actor = "principal-1";
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static ContactFields Name(string? given, string? middle, string? family, string? nickname, DisplayNameFormat format = DisplayNameFormat.Full) =>
        new(given, middle, family, nickname, null, null, null, null, null, format);

    private static long _seq;

    // Wrap an event payload as an IEvent<T> carrying a timestamp, actor header, and an ascending event id, as Marten
    // hydrates on replay. The id matters: SectionLww's unstamped fallback tiebreaks equal timestamps by event id.
    private static IEvent<T> Ev<T>(T data, DateTimeOffset? at = null, string? actor = Actor)
    {
        var e = Event.For(data);
        e.Id = new Guid(Interlocked.Increment(ref _seq).ToString("x32"));
        e.Timestamp = at ?? T0;
        if (actor is not null) e.Headers = new Dictionary<string, object> { [EventActor.HeaderKey] = actor };
        return e;
    }

    private static Contact Created(Guid id, ContactFields? fields = null)
    {
        var c = new Contact();
        c.Apply(Ev(new ContactCreated(id, Guid.NewGuid(), "u@x", fields ?? Name("A", null, "B", null))));
        return c;
    }

    [Fact]
    public void DisplayName_is_composed_from_name_parts()
    {
        var c = Created(Guid.NewGuid(), Name("Jane", "Q", "Smith", null));
        Assert.Equal("Jane Q Smith", c.DisplayName);
    }

    [Fact]
    public void DisplayName_falls_back_to_nickname()
    {
        var c = Created(Guid.NewGuid(), Name(null, null, null, "Mom"));
        Assert.Equal("Mom", c.DisplayName);
    }

    [Fact]
    public void DisplayName_FirstLast_uses_only_given_and_family()
    {
        var c = Created(Guid.NewGuid(), Name("Jane", "Q", "Smith", "Janie", DisplayNameFormat.FirstLast));
        Assert.Equal("Jane Smith", c.DisplayName);
    }

    [Fact]
    public void DisplayName_NickName_uses_the_nickname()
    {
        var c = Created(Guid.NewGuid(), Name("Jane", null, "Smith", "Janie", DisplayNameFormat.NickName));
        Assert.Equal("Janie", c.DisplayName);
    }

    [Fact]
    public void DisplayName_NickName_without_a_nickname_falls_back_to_the_full_composition()
    {
        var c = Created(Guid.NewGuid(), Name("Jane", null, "Smith", null, DisplayNameFormat.NickName));
        Assert.Equal("Jane Smith", c.DisplayName);
    }

    [Fact]
    public void SortName_stays_the_full_composition_regardless_of_format()
    {
        var full = new[] { DisplayNameFormat.Full, DisplayNameFormat.FirstLast, DisplayNameFormat.NickName }
            .Select(f => Created(Guid.NewGuid(), Name("Jane", "Q", "Smith", "Janie", f)).SortName);
        Assert.All(full, s => Assert.Equal("Jane Q Smith", s));
    }

    [Fact]
    public void SearchText_includes_the_nickname_even_when_name_parts_are_present()
    {
        var c = Created(Guid.NewGuid(), Name("Jane", null, "Smith", "Janie", DisplayNameFormat.NickName));
        Assert.Contains("Janie", c.SearchText);
        Assert.Contains("Smith", c.SearchText);
    }

    [Fact]
    public void DisplayNameFormat_change_via_revise_does_not_move_the_hash()
    {
        var id = Guid.NewGuid();
        var c = Created(id, Name("Jane", null, "Smith", "Janie"));
        var h0 = c.ContentHash;
        c.Apply(Ev(new ContactRevised(id, Name("Jane", null, "Smith", "Janie", DisplayNameFormat.NickName))));

        Assert.Equal("Janie", c.DisplayName);
        Assert.Equal(h0, c.ContentHash);   // rendering preference — hash-neutral
    }

    [Fact]
    public void Create_derives_a_content_hash_and_sets_created_attribution()
    {
        var c = Created(Guid.NewGuid());
        Assert.NotEmpty(c.ContentHash);
        Assert.Equal(T0, c.CreatedAt);
        Assert.Equal(Actor, c.CreatedBy);
        Assert.Equal(T0, c.UpdatedAt);
        Assert.Equal(Actor, c.UpdatedBy);
    }

    [Fact]
    public void Mutation_updates_the_updated_attribution_but_not_created()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        var t1 = T0.AddDays(1);
        c.Apply(Ev(new ContactRevised(id, Name("Robert", null, "Jones", null)), at: t1, actor: "principal-2"));

        Assert.Equal(T0, c.CreatedAt);
        Assert.Equal(Actor, c.CreatedBy);
        Assert.Equal(t1, c.UpdatedAt);
        Assert.Equal("principal-2", c.UpdatedBy);
    }

    [Fact]
    public void Deleted_records_the_event_timestamp_then_restored_clears_the_tombstone()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        var t1 = T0.AddHours(1);
        c.Apply(Ev(new ContactDeleted(id), at: t1));
        Assert.Equal(t1, c.DeletedAt);   // deterministic on replay: timestamp from event metadata, not a wall clock
        c.Apply(Ev(new ContactRestored(id)));
        Assert.Null(c.DeletedAt);
    }

    [Fact]
    public void VcardPut_resurrects_a_soft_deleted_contact()
    {
        var id = Guid.NewGuid();
        var book = Guid.NewGuid();
        var c = new Contact();
        c.Apply(Ev(new ContactCreated(id, book, "u@x", Name("A", null, "B", null))));
        c.Apply(Ev(new ContactDeleted(id)));

        c.Apply(Ev(new ContactImported(id, book, "u@x", Name("A", null, "B", null))));
        Assert.Null(c.DeletedAt);
        Assert.NotEmpty(c.ContentHash);
    }

    [Fact]
    public void Purge_discards_everything_the_deleted_contact_held()
    {
        var id = Guid.NewGuid();
        var book = Guid.NewGuid();
        var c = new Contact();
        c.Apply(Ev(new ContactCreated(id, book, "u@x",
            new ContactFields("Old", "Mid", "Name", "Nick", [new ContactReachChannel(ReachMedium.Email, "old@x", null, false)], new PartialDate(1990, 1, 1), ["family"], "note", "she/her", DisplayNameFormat.NickName, ContactKind.Organization)), at: T0.AddDays(-1), actor: "creator"));
        c.Apply(Ev(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "telegram", Handle = "old" }])));
        c.Apply(Ev(new ContactEmergencyContactsReplaced(id, [Guid.NewGuid()])));
        c.Apply(Ev(new ContactMarkedDeceased(id, new DateOnly(2020, 1, 1))));
        c.Apply(Ev(new ContactAvatarSet(id, "https://cdn/x.jpg")));
        c.Apply(Ev(new ContactMetadataAttached(id, "{\"k\":1}")));
        c.Apply(Ev(new ContactDeleted(id)));

        c.Apply(Ev(new ContactPurged(id), at: T0.AddHours(1)));
        c.Apply(Ev(new ContactImported(id, book, "u@x", Name("New", null, "Person", null, DisplayNameFormat.FirstLast)), at: T0.AddHours(1)));

        var fresh = Created(id, Name("New", null, "Person", null, DisplayNameFormat.FirstLast));
        Assert.Null(c.DeletedAt);
        Assert.Equal(("New", (string?) null, "Person", (string?) null), (c.GivenName, c.MiddleName, c.FamilyName, c.Nickname));
        Assert.Empty(c.Channels);
        Assert.Null(c.Birthday);
        Assert.Null(c.Tags);
        Assert.Null(c.Notes);
        Assert.Null(c.Pronouns);
        Assert.Equal(ContactKind.Individual, c.Kind);
        Assert.Empty(c.Profiles);
        Assert.Empty(c.EmergencyContactIds);
        Assert.False(c.Deceased);
        Assert.Null(c.DeathDate);
        Assert.Null(c.AvatarRef);
        Assert.Equal("{}", c.Metadata);
        Assert.Equal(T0.AddHours(1), c.CreatedAt);
        Assert.Equal(fresh.ContentHash, c.ContentHash);
    }

    [Fact]
    public void An_edit_stamped_before_the_purge_loses()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        c.Apply(Ev(new ContactDeleted(id)));
        c.Apply(Ev(new ContactPurged(id), at: T0.AddHours(2)));
        c.Apply(Ev(new ContactImported(id, c.AddressBookId, c.ExternalId, Name("New", null, null, null)), at: T0.AddHours(2)));

        c.Apply(Ev(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "telegram", Handle = "stale" }], T0.AddHours(1), Guid.NewGuid())));
        Assert.Empty(c.Profiles);
    }

    [Fact]
    public void Revised_updates_the_name_and_the_hash()
    {
        var id = Guid.NewGuid();
        var c = Created(id, Name("Bob", null, "Jones", null));
        var h0 = c.ContentHash;
        c.Apply(Ev(new ContactRevised(id, Name("Robert", null, "Jones", null))));

        Assert.Equal("Robert Jones", c.DisplayName);
        Assert.NotEqual(h0, c.ContentHash);
    }

    [Fact]
    public void Name_parts_are_trimmed_and_blank_parts_become_null()
    {
        var c = Created(Guid.NewGuid(), Name(" Anna ", "  ", " Weideskog", ""));

        Assert.Equal("Anna", c.GivenName);
        Assert.Null(c.MiddleName);
        Assert.Equal("Weideskog", c.FamilyName);
        Assert.Null(c.Nickname);
        Assert.Equal(Created(Guid.NewGuid(), Name("Anna", null, "Weideskog", null)).ContentHash, c.ContentHash);
    }

    [Fact]
    public void Profiles_replaced_is_wholesale_and_content_bearing()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        var h0 = c.ContentHash;

        c.Apply(Ev(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "mastodon", Handle = "@a" }])));
        c.Apply(Ev(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "github", Handle = "b", Url = "https://github.com/b", Preferred = true }])));

        var only = Assert.Single(c.Profiles);
        Assert.Equal("github", only.Service);
        Assert.Equal("https://github.com/b", only.Url);
        Assert.True(only.Preferred);
        Assert.NotEqual(h0, c.ContentHash);   // profiles are content-bearing
    }

    [Fact]
    public void EmergencyContacts_replaced_keeps_order_and_is_content_bearing()
    {
        var id = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var c = Created(id);
        var h0 = c.ContentHash;

        c.Apply(Ev(new ContactEmergencyContactsReplaced(id, [first, second])));
        Assert.Equal([first, second], c.EmergencyContactIds);   // order = priority
        Assert.NotEqual(h0, c.ContentHash);

        c.Apply(Ev(new ContactEmergencyContactsReplaced(id, [])));
        Assert.Empty(c.EmergencyContactIds);
    }

    [Fact]
    public void MarkedDeceased_sets_flag_and_date_and_cleared_resets_both()
    {
        var id = Guid.NewGuid();
        var c = Created(id);

        c.Apply(Ev(new ContactMarkedDeceased(id, new DateOnly(2020, 3, 14))));
        Assert.True(c.Deceased);
        Assert.Equal(new DateOnly(2020, 3, 14), c.DeathDate);

        c.Apply(Ev(new ContactDeceasedCleared(id)));
        Assert.False(c.Deceased);
        Assert.Null(c.DeathDate);
    }

    [Fact]
    public void MarkedDeceased_without_date_is_valid()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        c.Apply(Ev(new ContactMarkedDeceased(id, null)));
        Assert.True(c.Deceased);
        Assert.Null(c.DeathDate);
    }

    [Fact]
    public void Moved_records_the_book_it_left_but_not_the_etag()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        var (from, hash) = (c.AddressBookId, c.ContentHash);
        var to = Guid.NewGuid();

        c.Apply(Ev(new ContactMoved(id, to)));

        Assert.Equal(to, c.AddressBookId);
        Assert.Equal([from], c.FormerAddressBookIds);
        Assert.Equal(hash, c.ContentHash);
    }

    [Fact]
    public void Recomputed_hash_is_deterministic_for_identical_state()
    {
        var id = Guid.NewGuid();
        var a = Created(id, Name("Sam", null, "Vimes", null));
        var b = Created(id, Name("Sam", null, "Vimes", null));
        Assert.Equal(a.ContentHash, b.ContentHash);
    }

    [Theory]
    [InlineData(ContactRelationKind.Parent, ContactRelationKind.Child)]
    [InlineData(ContactRelationKind.Child, ContactRelationKind.Parent)]
    [InlineData(ContactRelationKind.Grandparent, ContactRelationKind.Grandchild)]
    [InlineData(ContactRelationKind.Grandchild, ContactRelationKind.Grandparent)]
    [InlineData(ContactRelationKind.AuntUncle, ContactRelationKind.NieceNephew)]
    [InlineData(ContactRelationKind.NieceNephew, ContactRelationKind.AuntUncle)]
    [InlineData(ContactRelationKind.Sibling, ContactRelationKind.Sibling)]
    [InlineData(ContactRelationKind.Cousin, ContactRelationKind.Cousin)]
    [InlineData(ContactRelationKind.Spouse, ContactRelationKind.Spouse)]
    [InlineData(ContactRelationKind.Partner, ContactRelationKind.Partner)]
    [InlineData(ContactRelationKind.Friend, ContactRelationKind.Friend)]
    [InlineData(ContactRelationKind.Colleague, ContactRelationKind.Colleague)]
    [InlineData(ContactRelationKind.Neighbor, ContactRelationKind.Neighbor)]
    [InlineData(ContactRelationKind.Other, ContactRelationKind.Other)]
    public void Relation_kind_inverse_matrix(ContactRelationKind kind, ContactRelationKind expected) =>
        Assert.Equal(expected, kind.Inverse());
}
