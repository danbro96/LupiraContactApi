using JasperFx.Events;

namespace LupiraContactApi.Domain;

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
    public List<Guid> MemberContactIds { get; set; } = new();
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
        if (!MemberContactIds.Contains(e.Data.ContactId)) MemberContactIds.Add(e.Data.ContactId);
        Touch(e);
    }

    public void Apply(IEvent<ContactRemovedFromGroup> e)
    {
        MemberContactIds.Remove(e.Data.ContactId);
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
