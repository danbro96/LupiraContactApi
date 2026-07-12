using JasperFx.Events;
using static LupiraContactApi.Domain.ContentHash;   // Of(); the type name clashes with the ContentHash property

namespace LupiraContactApi.Domain;

/// <summary>A contact's postal address: a LupiraGeoApi place id (the sole source of truth — no free-text) with a home/work
/// type. The write path requires a real id; the property is nullable only so legacy pre-migration events (which carried a
/// null id + free-text) still deserialize.</summary>
public sealed class ContactPostalAddress
{
    public Guid? PlaceId { get; set; }
    public ContactAddressType Type { get; set; }
}

/// <summary>A social/IM handle. <c>Service</c> is an open string (platforms are unbounded); <c>Preferred</c> marks
/// the handle that actually reaches the person on that service.</summary>
public sealed class ContactSocialProfile
{
    public string Service { get; set; } = "";
    public string Handle { get; set; } = "";
    public string? Url { get; set; }
    public bool Preferred { get; set; }
}

/// <summary>
/// The contact aggregate + inline snapshot, belonging to one address book. The structured fields are canonical;
/// <c>ContentHash</c> is derived from them (see <see cref="ContactContent"/>) after every content change and serves
/// sync surfaces as an opaque version tag — it is never carried on events. Attribution (created/updated by/at) comes
/// from Marten event metadata (see <see cref="EventActor"/>).
/// </summary>
public sealed class Contact
{
    public Guid Id { get; set; }
    public Guid AddressBookId { get; set; }
    public string ExternalId { get; set; } = "";

    public string? NamePrefix { get; set; }
    public string? GivenName { get; set; }
    public string? MiddleName { get; set; }
    public string? FamilyName { get; set; }
    public string? NameSuffix { get; set; }
    public string? Nickname { get; set; }
    public DisplayNameFormat DisplayNameFormat { get; set; }
    public List<ContactReachChannel> Channels { get; set; } = new();
    public PartialDate? Birthday { get; set; }
    public string[]? Tags { get; set; }
    public string? Notes { get; set; }
    public string? Pronouns { get; set; }

    /// <summary>A pointer to an avatar image (URL/media id) — never bytes. Outside the canonical content, like <see cref="Addresses"/>.</summary>
    public string? AvatarRef { get; set; }

    public string ContentHash { get; set; } = "";
    public string Metadata { get; set; } = "{}";

    public List<ContactPostalAddress> Addresses { get; set; } = new();
    public List<ContactSocialProfile> Profiles { get; set; } = new();
    public List<ContactRelation> Relations { get; set; } = new();

    /// <summary>Ordered designation (first = highest priority) — who to call about this person, not a kinship.</summary>
    public List<Guid> EmergencyContactIds { get; set; } = new();

    public bool Deceased { get; set; }
    public DateOnly? DeathDate { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Composed display label, per <see cref="DisplayNameFormat"/>. Falls back to the full composition, then nickname, then external id — never empty.</summary>
    public string DisplayName
    {
        get
        {
            var label = DisplayNameFormat switch
            {
                DisplayNameFormat.FirstLast => string.Join(' ', new[] { GivenName, FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s))),
                DisplayNameFormat.NickName => Nickname ?? "",
                _ => "",   // Full → the full composition below
            };
            return string.IsNullOrWhiteSpace(label) ? ComposeFull() : label;
        }
    }

    /// <summary>Stable full-name composition for ordering — independent of <see cref="DisplayNameFormat"/>.</summary>
    public string SortName => ComposeFull();

    /// <summary>Every name token plus the nickname, for search matching — a contact is findable by nickname or real name regardless of the display format.</summary>
    public string SearchText => string.Join(' ', new[] { NamePrefix, GivenName, MiddleName, FamilyName, NameSuffix, Nickname }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    private string ComposeFull()
    {
        var name = string.Join(' ', new[] { NamePrefix, GivenName, MiddleName, FamilyName, NameSuffix }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length > 0 ? name : (Nickname ?? ExternalId);
    }

    public void Apply(IEvent<ContactCreated> e)
    {
        var d = e.Data;
        Id = d.ContactId;
        AddressBookId = d.AddressBookId;
        ExternalId = d.ExternalId;
        SetFields(d.Fields);
        DeletedAt = null;
        Created(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactImported> e)
    {
        var d = e.Data;
        Id = d.ContactId;
        AddressBookId = d.AddressBookId;
        ExternalId = d.ExternalId;
        SetFields(d.Parsed);
        DeletedAt = null;
        if (CreatedAt == default) Created(e); else Touch(e);   // import is create-or-replace (also the resurrection path)
        RecomputeHash();
    }

    public void Apply(IEvent<ContactRevised> e)
    {
        SetFields(e.Data.Fields);
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactDeleted> e)
    {
        DeletedAt = e.Timestamp;
        Touch(e);
    }

    public void Apply(IEvent<ContactRestored> e)
    {
        DeletedAt = null;
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactAddressesReplaced> e)
    {
        Addresses = e.Data.Addresses.Select(a => new ContactPostalAddress { PlaceId = a.PlaceId, Type = a.Type }).ToList();
        Touch(e);   // addresses are outside the canonical content — no RecomputeHash, ETag unchanged
    }

    public void Apply(IEvent<ContactProfilesReplaced> e)
    {
        Profiles = e.Data.Profiles.Select(p => new ContactSocialProfile { Service = p.Service, Handle = p.Handle, Url = p.Url, Preferred = p.Preferred }).ToList();
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactEmergencyContactsReplaced> e)
    {
        EmergencyContactIds = [.. e.Data.ContactIds];
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactMarkedDeceased> e)
    {
        Deceased = true;
        DeathDate = e.Data.DeathDate;
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactDeceasedCleared> e)
    {
        Deceased = false;
        DeathDate = null;
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactAvatarSet> e)
    {
        AvatarRef = string.IsNullOrWhiteSpace(e.Data.Ref) ? null : e.Data.Ref.Trim();
        Touch(e);   // avatar is a mutable pointer outside the canonical content — no RecomputeHash, ETag unchanged
    }

    public void Apply(IEvent<ContactRelationAdded> e)
    {
        var d = e.Data;
        Relations.RemoveAll(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind);   // upsert on the natural key; also revives an ended edge
        Relations.Add(new ContactRelation { ToContactId = d.ToContactId, Kind = d.Kind, Label = d.Label, Since = d.Since, Note = d.Note });
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactRelationEnded> e)
    {
        var d = e.Data;
        var edge = Relations.FirstOrDefault(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind);
        if (edge is not null) { edge.Ended = true; edge.Until = d.Until; }
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactRelationRemoved> e)
    {
        var d = e.Data;
        Relations.RemoveAll(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind);
        Touch(e);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactRelationsReplaced> e)
    {
        Relations = e.Data.Relations.Select(r => new ContactRelation { ToContactId = r.ToContactId, Kind = r.Kind, Label = r.Label, Since = r.Since, Note = r.Note, Ended = r.Ended, Until = r.Until }).ToList();
        Touch(e);
        RecomputeHash();
    }

    private void Created(IEvent e)
    {
        CreatedAt = e.Timestamp;
        CreatedBy = EventActor.Of(e);
        Touch(e);
    }

    private void Touch(IEvent e)
    {
        UpdatedAt = e.Timestamp;
        UpdatedBy = EventActor.Of(e);
    }

    /// <summary>Derives <see cref="ContentHash"/> from the current content-bearing state. Called after every content change;
    /// the single caller of <see cref="ContactContent"/>, so a canonicalization fix heals every snapshot on rebuild.</summary>
    private void RecomputeHash() =>
        ContentHash = Of(ContactContent.Canonical(ExternalId, Fields(), Relations, EmergencyContactIds, Profiles, Deceased, DeathDate));

    private ContactFields Fields() =>
        new(NamePrefix, GivenName, MiddleName, FamilyName, NameSuffix, Nickname, Channels, Birthday, Tags, Notes, Pronouns, DisplayNameFormat);

    private void SetFields(ContactFields f)
    {
        NamePrefix = f.NamePrefix;
        GivenName = f.GivenName;
        MiddleName = f.MiddleName;
        FamilyName = f.FamilyName;
        NameSuffix = f.NameSuffix;
        Nickname = f.Nickname;
        DisplayNameFormat = f.DisplayNameFormat;
        Channels = f.Channels is null ? [] : [.. f.Channels];   // channels are vCard-authoritative (wholesale, like the old Emails/Phones)
        Birthday = f.Birthday;
        Notes = f.Notes;
        Pronouns = f.Pronouns;
        if (f.Tags is not null) Tags = f.Tags;   // tags are Lupira-only (not in the imported card) — preserve when unmentioned
    }
}
