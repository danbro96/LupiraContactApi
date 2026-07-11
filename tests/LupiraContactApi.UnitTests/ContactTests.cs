using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Event-replay behavior of the <see cref="Contact"/> aggregate: composed display name, soft-delete +
/// resurrection, structured revision, and wholesale replacement of postal addresses / social profiles.</summary>
public class ContactTests
{
    static ContactFields Name(string? prefix, string? given, string? middle, string? family, string? suffix, string? nickname) =>
        new(prefix, given, middle, family, suffix, nickname, null, null, null, null);

    [Fact]
    public void DisplayName_is_composed_from_name_parts()
    {
        var c = new Contact();
        c.Apply(new ContactCreated(Guid.NewGuid(), Guid.NewGuid(), "u@x",
            Name("Dr", "Jane", "Q", "Smith", "Jr", null), "h"));
        Assert.Equal("Dr Jane Q Smith Jr", c.DisplayName);
    }

    [Fact]
    public void DisplayName_falls_back_to_nickname()
    {
        var c = new Contact();
        c.Apply(new ContactCreated(Guid.NewGuid(), Guid.NewGuid(), "u@x",
            Name(null, null, null, null, null, "Mom"), "h"));
        Assert.Equal("Mom", c.DisplayName);
    }

    static readonly DateTimeOffset DeletedStamp = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Deleted_then_restored_clears_the_tombstone()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h"));
        c.Apply(new ContactDeleted(id, DeletedStamp));
        Assert.Equal(DeletedStamp, c.DeletedAt);   // deterministic on replay: the timestamp lives on the event
        c.Apply(new ContactRestored(id, "h2"));
        Assert.Null(c.DeletedAt);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void VcardPut_resurrects_a_soft_deleted_contact()
    {
        var id = Guid.NewGuid();
        var book = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, book, "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactDeleted(id, DeletedStamp));

        c.Apply(new ContactImported(id, book, "u@x", Name(null, "A", null, "B", null, null), "h2"));
        Assert.Null(c.DeletedAt);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void Revised_updates_the_name_and_hash()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "Bob", null, "Jones", null, null), "h1"));
        c.Apply(new ContactRevised(id, Name(null, "Robert", null, "Jones", null, null), "h2"));

        Assert.Equal("Robert Jones", c.DisplayName);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void Addresses_replaced_is_wholesale_not_additive()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h"));

        c.Apply(new ContactAddressesReplaced(id, [new ContactPostalAddress { PlaceId = Guid.NewGuid(), Type = ContactAddressType.Home }]));
        Assert.Single(c.Addresses);

        var work = Guid.NewGuid();
        c.Apply(new ContactAddressesReplaced(id, [new ContactPostalAddress { PlaceId = work, Type = ContactAddressType.Work }]));
        var only = Assert.Single(c.Addresses);            // replaced, not appended
        Assert.Equal(work, only.PlaceId);
        Assert.Equal(ContactAddressType.Work, only.Type);
    }

    [Fact]
    public void Profiles_replaced_is_wholesale_not_additive()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h"));

        c.Apply(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "mastodon", Handle = "@a" }], "h2"));
        c.Apply(new ContactProfilesReplaced(id, [new ContactSocialProfile { Service = "github", Handle = "b", Url = "https://github.com/b", Preferred = true }], "h3"));

        var only = Assert.Single(c.Profiles);
        Assert.Equal("github", only.Service);
        Assert.Equal("https://github.com/b", only.Url);
        Assert.True(only.Preferred);
        Assert.Equal("h3", c.ContentHash);   // profiles are content-bearing
    }

    [Fact]
    public void EmergencyContacts_replaced_keeps_order_and_updates_the_hash()
    {
        var id = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));

        c.Apply(new ContactEmergencyContactsReplaced(id, [first, second], "h2"));
        Assert.Equal([first, second], c.EmergencyContactIds);   // order = priority
        Assert.Equal("h2", c.ContentHash);

        c.Apply(new ContactEmergencyContactsReplaced(id, [], "h3"));
        Assert.Empty(c.EmergencyContactIds);
    }

    [Fact]
    public void MarkedDeceased_sets_flag_and_date_and_cleared_resets_both()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));

        c.Apply(new ContactMarkedDeceased(id, new DateOnly(2020, 3, 14), "h2"));
        Assert.True(c.Deceased);
        Assert.Equal(new DateOnly(2020, 3, 14), c.DeathDate);
        Assert.Equal("h2", c.ContentHash);

        c.Apply(new ContactDeceasedCleared(id, "h3"));
        Assert.False(c.Deceased);
        Assert.Null(c.DeathDate);
        Assert.Equal("h3", c.ContentHash);
    }

    [Fact]
    public void MarkedDeceased_without_date_is_valid()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));

        c.Apply(new ContactMarkedDeceased(id, null, "h2"));
        Assert.True(c.Deceased);
        Assert.Null(c.DeathDate);
    }

    [Fact]
    public void RelationAdded_appends_the_edge_and_updates_the_hash()
    {
        var id = Guid.NewGuid();
        var dad = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));

        c.Apply(new ContactRelationAdded(id, dad, ContactRelationKind.Parent, "dad", "h2"));

        var edge = Assert.Single(c.Relations);
        Assert.Equal(dad, edge.ToContactId);
        Assert.Equal(ContactRelationKind.Parent, edge.Kind);
        Assert.Equal("dad", edge.Label);
        Assert.Equal("h2", c.ContentHash);
    }

    [Fact]
    public void RelationAdded_upserts_on_target_and_kind_but_keeps_other_kinds()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));

        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Parent, "dad", "h2"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Friend, null, "h3"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Parent, "father", "h4"));

        Assert.Equal(2, c.Relations.Count);
        Assert.Equal("father", c.Relations.Single(r => r.Kind == ContactRelationKind.Parent).Label);
        Assert.Null(c.Relations.Single(r => r.Kind == ContactRelationKind.Friend).Label);
        Assert.Equal("h4", c.ContentHash);
    }

    [Fact]
    public void RelationRemoved_deletes_only_the_matching_kind()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Friend, null, "h2"));
        c.Apply(new ContactRelationAdded(id, other, ContactRelationKind.Colleague, null, "h3"));

        c.Apply(new ContactRelationRemoved(id, other, ContactRelationKind.Friend, "h4"));

        var edge = Assert.Single(c.Relations);
        Assert.Equal(ContactRelationKind.Colleague, edge.Kind);
        Assert.Equal("h4", c.ContentHash);
    }

    [Fact]
    public void RelationsReplaced_is_wholesale_not_additive()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactRelationAdded(id, Guid.NewGuid(), ContactRelationKind.Parent, "dad", "h2"));

        var sis = Guid.NewGuid();
        c.Apply(new ContactRelationsReplaced(id, [new ContactRelation { ToContactId = sis, Kind = ContactRelationKind.Sibling }]));

        var edge = Assert.Single(c.Relations);
        Assert.Equal(sis, edge.ToContactId);
        Assert.Equal(ContactRelationKind.Sibling, edge.Kind);
    }

    [Fact]
    public void RelationEnded_flags_the_edge_and_readding_revives_it()
    {
        var id = Guid.NewGuid();
        var ex = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactRelationAdded(id, ex, ContactRelationKind.Spouse, null, "h2"));

        c.Apply(new ContactRelationEnded(id, ex, ContactRelationKind.Spouse, new DateOnly(2024, 6, 1), "h3"));
        var edge = Assert.Single(c.Relations);
        Assert.True(edge.Ended);
        Assert.Equal(new DateOnly(2024, 6, 1), edge.Until);
        Assert.Equal("h3", c.ContentHash);

        c.Apply(new ContactRelationAdded(id, ex, ContactRelationKind.Spouse, null, "h4"));   // remarried
        edge = Assert.Single(c.Relations);
        Assert.False(edge.Ended);
        Assert.Null(edge.Until);
    }

    [Fact]
    public void RelationEnded_for_a_missing_edge_is_a_noop_on_the_edges()
    {
        var id = Guid.NewGuid();
        var c = new Contact();
        c.Apply(new ContactCreated(id, Guid.NewGuid(), "u@x", Name(null, "A", null, "B", null, null), "h1"));
        c.Apply(new ContactRelationEnded(id, Guid.NewGuid(), ContactRelationKind.Friend, null, "h2"));
        Assert.Empty(c.Relations);
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
