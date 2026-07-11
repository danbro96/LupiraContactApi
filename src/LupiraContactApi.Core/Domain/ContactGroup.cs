using JasperFx.Events;

namespace LupiraContactApi.Domain;

/// <summary>One contact's membership in a group. <see cref="Role"/> is the title held in an <c>Organization</c>
/// (null for personal groupings); <see cref="Since"/>/<see cref="Until"/> bound the tenure when known.</summary>
public sealed class GroupMembership
{
    public Guid ContactId { get; set; }
    public string? Role { get; set; }
    public DateOnly? Since { get; set; }
    public DateOnly? Until { get; set; }
}

/// <summary>
/// A named collection of contacts in an address book + inline snapshot. <see cref="Kind"/> distinguishes a personal
/// grouping (Friends/Family/Colleagues) from an <c>Organization</c> (a company — a contact's employer is membership
/// here, not a free-text field). Membership add/remove are events, so "when X joined" is retained as history
/// (the event metadata timestamp). Attribution comes from event metadata (see <see cref="EventActor"/>).
/// </summary>
public sealed class ContactGroup
{
    public Guid Id { get; set; }
    public Guid AddressBookId { get; set; }
    public ContactGroupKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? ExternalId { get; set; }
    public List<GroupMembership> Members { get; set; } = new();
    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public void Apply(IEvent<ContactGroupCreated> e)
    {
        var d = e.Data;
        Id = d.GroupId;
        AddressBookId = d.AddressBookId;
        Kind = d.Kind;
        Name = d.Name;
        ExternalId = d.ExternalId;
        CreatedAt = e.Timestamp;
        CreatedBy = EventActor.Of(e);
        Touch(e);
    }

    public void Apply(IEvent<ContactGroupRenamed> e)
    {
        Name = e.Data.Name;
        Touch(e);
    }

    public void Apply(IEvent<ContactAddedToGroup> e)
    {
        var d = e.Data;
        Members.RemoveAll(m => m.ContactId == d.ContactId);   // upsert on ContactId — re-add updates role/dates
        Members.Add(new GroupMembership { ContactId = d.ContactId, Role = d.Role, Since = d.Since, Until = d.Until });
        Touch(e);
    }

    public void Apply(IEvent<ContactRemovedFromGroup> e)
    {
        Members.RemoveAll(m => m.ContactId == e.Data.ContactId);
        Touch(e);
    }

    public void Apply(IEvent<ContactGroupDeleted> e)
    {
        DeletedAt = e.Timestamp;
        Touch(e);
    }

    private void Touch(IEvent e)
    {
        UpdatedAt = e.Timestamp;
        UpdatedBy = EventActor.Of(e);
    }
}
