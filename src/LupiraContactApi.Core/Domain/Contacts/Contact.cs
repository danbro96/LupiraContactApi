using JasperFx.Events;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Shared;
using static LupiraContactApi.Core.Domain.Shared.ContentHash;   // Of(); the type name clashes with the ContentHash property

namespace LupiraContactApi.Core.Domain.Contacts;

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

    /// <summary>Books this contact was moved out of, so the sync feed can still tombstone it to their readers.</summary>
    public List<Guid> FormerAddressBookIds { get; set; } = new();

    public string ExternalId { get; set; } = string.Empty;

    public ContactKind Kind { get; set; }

    public string? GivenName { get; set; }

    public string? MiddleName { get; set; }

    public string? FamilyName { get; set; }

    public string? Nickname { get; set; }

    public DisplayNameFormat DisplayNameFormat { get; set; }

    public List<ContactReachChannel> Channels { get; set; } = new();

    public PartialDate? Birthday { get; set; }

    public string[]? Tags { get; set; }

    public string? Notes { get; set; }

    public string? Pronouns { get; set; }

    /// <summary>A pointer to an avatar image (URL/media id) — never bytes. Outside the canonical content, like <see cref="Addresses"/>.</summary>
    public string? AvatarRef { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public string Metadata { get; set; } = "{}";

    public List<ContactPostalAddress> Addresses { get; set; } = new();

    public List<ContactSocialProfile> Profiles { get; set; } = new();

    /// <summary>Ordered designation (first = highest priority) — who to call about this person, not a kinship.</summary>
    public List<Guid> EmergencyContactIds { get; set; } = new();

    public bool Deceased { get; set; }

    public DateOnly? DeathDate { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    /// <summary>Global event sequence of the last event applied — the per-contact watermark the sync changes
    /// feed queries by (indexed). Bumped on every event, even one whose section guard rejects it.</summary>
    public long UpdatedSequence { get; set; }

    /// <summary>Stream version, populated by Marten's aggregate versioning.</summary>
    public int Version { get; set; }

    // ---- per-section LWW guards (see SectionLww): the (occurredAt, commandId) of each section's last winner.
    // Core covers ContactFields wholesale — channels and tags ride inside ContactRevised, so they share it. ----

    public DateTimeOffset CoreTs { get; set; }

    public Guid CoreCmd { get; set; }

    public DateTimeOffset AddressesTs { get; set; }

    public Guid AddressesCmd { get; set; }

    public DateTimeOffset ProfilesTs { get; set; }

    public Guid ProfilesCmd { get; set; }

    public DateTimeOffset AvatarTs { get; set; }

    public Guid AvatarCmd { get; set; }

    public DateTimeOffset MetadataTs { get; set; }

    public Guid MetadataCmd { get; set; }

    public DateTimeOffset DeceasedTs { get; set; }

    public Guid DeceasedCmd { get; set; }

    /// <summary>Composed display label, per <see cref="DisplayNameFormat"/>. Falls back to the full composition, then nickname, then external id — never empty.</summary>
    public string DisplayName
    {
        get
        {
            var label = DisplayNameFormat switch
            {
                DisplayNameFormat.FirstLast => string.Join(' ', new[] { GivenName, FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s))),
                DisplayNameFormat.NickName => Nickname ?? string.Empty,
                _ => string.Empty,   // Full → the full composition below
            };
            return string.IsNullOrWhiteSpace(label) ? ComposeFull() : label;
        }
    }

    /// <summary>Stable full-name composition for ordering — independent of <see cref="DisplayNameFormat"/>.</summary>
    public string SortName => ComposeFull();

    /// <summary>Every name token plus the nickname, for search matching — a contact is findable by nickname or real name regardless of the display format.</summary>
    public string SearchText => string.Join(' ', new[] { GivenName, MiddleName, FamilyName, Nickname }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    private string ComposeFull()
    {
        var name = string.Join(' ', new[] { GivenName, MiddleName, FamilyName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length > 0 ? name : (Nickname ?? ExternalId);
    }

    public void Apply(IEvent<ContactCreated> e)
    {
        var d = e.Data;
        Id = d.ContactId;
        MoveTo(d.AddressBookId);
        ExternalId = d.ExternalId;
        SetFields(d.Fields);
        DeletedAt = null;
        Created(e);
        // Creation seeds the core guard from the server stamp: a stale offline edit predating the create loses.
        (CoreTs, CoreCmd) = SectionLww.Stamp(e, null, null);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactImported> e)
    {
        var d = e.Data;
        Id = d.ContactId;
        MoveTo(d.AddressBookId);
        ExternalId = d.ExternalId;
        SetFields(d.Parsed);
        DeletedAt = null;
        if (CreatedAt == default) Created(e); else Touch(e);   // import is create-or-replace (also the resurrection path)
        (CoreTs, CoreCmd) = SectionLww.Stamp(e, null, null);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactRevised> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, CoreTs, CoreCmd)) return;
        SetFields(e.Data.Fields);
        (CoreTs, CoreCmd) = (ts, cmd);
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

    public void Apply(IEvent<ContactMoved> e)
    {
        MoveTo(e.Data.AddressBookId);
        Touch(e);
    }

    public void Apply(IEvent<ContactAddressesReplaced> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, AddressesTs, AddressesCmd)) return;
        Addresses = e.Data.Addresses.Select(a => new ContactPostalAddress { PlaceId = a.PlaceId, Type = a.Type, MovedIn = a.MovedIn, MovedOut = a.MovedOut }).ToList();
        (AddressesTs, AddressesCmd) = (ts, cmd);
        // addresses are outside the canonical content — no RecomputeHash, ETag unchanged
    }

    public void Apply(IEvent<ContactProfilesReplaced> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, ProfilesTs, ProfilesCmd)) return;
        Profiles = e.Data.Profiles.Select(p => new ContactSocialProfile { Service = p.Service, Handle = p.Handle, Url = p.Url, Preferred = p.Preferred }).ToList();
        (ProfilesTs, ProfilesCmd) = (ts, cmd);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactEmergencyContactsReplaced> e)
    {
        EmergencyContactIds = [.. e.Data.ContactIds];
        Touch(e);
        RecomputeHash();
    }

    // Mark/clear deceased compete for the same state, so they share one guard and resolve against each other.
    public void Apply(IEvent<ContactMarkedDeceased> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, DeceasedTs, DeceasedCmd)) return;
        Deceased = true;
        DeathDate = e.Data.DeathDate;
        (DeceasedTs, DeceasedCmd) = (ts, cmd);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactDeceasedCleared> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, DeceasedTs, DeceasedCmd)) return;
        Deceased = false;
        DeathDate = null;
        (DeceasedTs, DeceasedCmd) = (ts, cmd);
        RecomputeHash();
    }

    public void Apply(IEvent<ContactMetadataAttached> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, MetadataTs, MetadataCmd)) return;
        Metadata = e.Data.MetadataJson;
        (MetadataTs, MetadataCmd) = (ts, cmd);   // annotation only — outside the canonical content, ETag unchanged
    }

    public void Apply(IEvent<ContactAvatarSet> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, AvatarTs, AvatarCmd)) return;
        AvatarRef = string.IsNullOrWhiteSpace(e.Data.Ref) ? null : e.Data.Ref.Trim();
        (AvatarTs, AvatarCmd) = (ts, cmd);   // avatar is a mutable pointer outside the canonical content — ETag unchanged
    }

    private void MoveTo(Guid addressBookId)
    {
        if (AddressBookId != Guid.Empty && AddressBookId != addressBookId && !FormerAddressBookIds.Contains(AddressBookId))
            FormerAddressBookIds.Add(AddressBookId);
        AddressBookId = addressBookId;
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
        UpdatedSequence = e.Sequence;
    }

    /// <summary>Derives <see cref="ContentHash"/> from the current content-bearing state. Called after every content change;
    /// the single caller of <see cref="ContactContent"/>, so a canonicalization fix heals every snapshot on rebuild.</summary>
    private void RecomputeHash() =>
        ContentHash = Of(ContactContent.Canonical(ExternalId, Fields(), EmergencyContactIds, Profiles, Deceased, DeathDate));

    /// <summary>The current structured fields — the base a section edit rebuilds from, so it carries every field the event does.</summary>
    internal ContactFields Fields() =>
        new(GivenName, MiddleName, FamilyName, Nickname, Channels, Birthday, Tags, Notes, Pronouns, DisplayNameFormat, Kind);

    private void SetFields(ContactFields f)
    {
        Kind = f.Kind;
        GivenName = f.GivenName;
        MiddleName = f.MiddleName;
        FamilyName = f.FamilyName;
        Nickname = f.Nickname;
        DisplayNameFormat = f.DisplayNameFormat;
        Channels = f.Channels is null ? [] : [.. f.Channels];   // channels are vCard-authoritative (wholesale, like the old Emails/Phones)
        Birthday = f.Birthday;
        Notes = f.Notes;
        Pronouns = f.Pronouns;
        if (f.Tags is not null) Tags = f.Tags;   // tags are Lupira-only (not in the imported card) — preserve when unmentioned
    }
}
