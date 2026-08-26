using JasperFx.Events;
using LupiraContactApi.Core.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Event-replay behavior of the <see cref="ContactGroup"/> aggregate: creation, rename, soft-delete,
/// idempotent membership add/remove, and metadata attribution.</summary>
public class ContactGroupTests
{
    const string Actor = "principal-1";
    static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    static IEvent<T> Ev<T>(T data, DateTimeOffset? at = null, string? actor = Actor)
    {
        var e = Event.For(data);
        e.Timestamp = at ?? T0;
        if (actor is not null) e.Headers = new Dictionary<string, object> { [EventActor.HeaderKey] = actor };
        return e;
    }

    static ContactGroup Created(Guid gid, ContactGroupKind kind = ContactGroupKind.Organization, string name = "Acme")
    {
        var g = new ContactGroup();
        g.Apply(Ev(new ContactGroupCreated(gid, Guid.NewGuid(), kind, name, null)));
        return g;
    }

    [Fact]
    public void Organization_membership_add_and_remove()
    {
        var gid = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var g = Created(gid);
        Assert.Equal(ContactGroupKind.Organization, g.Kind);

        g.Apply(Ev(new ContactAddedToGroup(gid, contact, "Engineer")));
        Assert.Contains(g.Members, m => m.ContactId == contact && m.Role == "Engineer");

        g.Apply(Ev(new ContactRemovedFromGroup(gid, contact)));
        Assert.DoesNotContain(g.Members, m => m.ContactId == contact);
    }

    [Fact]
    public void Created_sets_attribution()
    {
        var g = Created(Guid.NewGuid());
        Assert.Equal(T0, g.CreatedAt);
        Assert.Equal(Actor, g.CreatedBy);
        Assert.Equal(T0, g.UpdatedAt);
        Assert.Equal(Actor, g.UpdatedBy);
    }

    [Fact]
    public void Personal_group_kind_is_preserved()
    {
        var g = Created(Guid.NewGuid(), ContactGroupKind.Group, "Family");
        Assert.Equal(ContactGroupKind.Group, g.Kind);
        Assert.Equal("Family", g.Name);
    }

    [Fact]
    public void Renamed_changes_the_name_and_updated_attribution()
    {
        var gid = Guid.NewGuid();
        var g = Created(gid);
        var t1 = T0.AddDays(1);
        g.Apply(Ev(new ContactGroupRenamed(gid, "Acme Corp"), at: t1, actor: "principal-2"));
        Assert.Equal("Acme Corp", g.Name);
        Assert.Equal(t1, g.UpdatedAt);
        Assert.Equal("principal-2", g.UpdatedBy);
    }

    [Fact]
    public void Deleted_records_the_event_timestamp()
    {
        var gid = Guid.NewGuid();
        var t1 = T0.AddHours(2);
        var g = Created(gid);
        g.Apply(Ev(new ContactGroupDeleted(gid), at: t1));
        Assert.Equal(t1, g.DeletedAt);   // deterministic on replay
    }

    [Fact]
    public void Duplicate_add_is_idempotent()
    {
        var gid = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var g = Created(gid);
        g.Apply(Ev(new ContactAddedToGroup(gid, contact)));
        g.Apply(Ev(new ContactAddedToGroup(gid, contact, "Lead")));   // re-add upserts (updates the role)
        var m = Assert.Single(g.Members);
        Assert.Equal("Lead", m.Role);
    }

    [Fact]
    public void Removing_a_non_member_is_a_no_op()
    {
        var gid = Guid.NewGuid();
        var g = Created(gid);
        g.Apply(Ev(new ContactRemovedFromGroup(gid, Guid.NewGuid())));
        Assert.Empty(g.Members);
    }
}
