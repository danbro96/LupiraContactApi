using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;

namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>One section's last-writer guard: the (occurredAt, commandId) of the write that owns its current
/// value. Offline clients seed their local guards from these so a pending edit on one section never blocks —
/// and is never clobbered by — fresher server state on another.</summary>
public sealed class SectionGuardDto
{
    public required DateTimeOffset Ts { get; set; }
    public required Guid Cmd { get; set; }

    internal static SectionGuardDto From(DateTimeOffset ts, Guid cmd) => new() { Ts = ts, Cmd = cmd };
}

/// <summary>Per-section guards for a contact. <c>Core</c> covers the whole ContactFields (name/channels/tags/
/// notes… — channel and tag writes ride the same revision event); the rest map 1:1 to their write endpoints.</summary>
public sealed class SectionGuardsDto
{
    public required SectionGuardDto Core { get; set; }
    public required SectionGuardDto Addresses { get; set; }
    public required SectionGuardDto Profiles { get; set; }
    public required SectionGuardDto Avatar { get; set; }
    public required SectionGuardDto Metadata { get; set; }
    public required SectionGuardDto Deceased { get; set; }

    internal static SectionGuardsDto From(Contact c) => new()
    {
        Core = SectionGuardDto.From(c.CoreTs, c.CoreCmd),
        Addresses = SectionGuardDto.From(c.AddressesTs, c.AddressesCmd),
        Profiles = SectionGuardDto.From(c.ProfilesTs, c.ProfilesCmd),
        Avatar = SectionGuardDto.From(c.AvatarTs, c.AvatarCmd),
        Metadata = SectionGuardDto.From(c.MetadataTs, c.MetadataCmd),
        Deceased = SectionGuardDto.From(c.DeceasedTs, c.DeceasedCmd),
    };
}

/// <summary>A changed contact: the full DTO plus its section guards.</summary>
public sealed class SyncChangeDto
{
    public required ContactDto Contact { get; set; }
    public required SectionGuardsDto Guards { get; set; }
}

/// <summary>One page of the changes feed. <c>Cursor</c> is opaque — hand it back as <c>?since=</c>; loop while
/// <c>HasMore</c>. A full sync (no <c>since</c>) streams every live visible contact; tombstone ids may reference
/// contacts the client never saw (ignore unknown ids).</summary>
public sealed class SyncChangesResponse
{
    public required string Cursor { get; set; }
    public required bool HasMore { get; set; }
    public required IReadOnlyList<SyncChangeDto> Changed { get; set; }

    /// <summary>Ids no longer visible to the caller: soft-deleted, or moved into an address book the caller
    /// can't read. Unknown ids are safe to ignore.</summary>
    public required IReadOnlyList<Guid> Deleted { get; set; }
}

/// <summary>Snapshot of the caller's containers. Address books are plain documents (no cursor); groups are
/// event-sourced but small and fully replaced each cycle — both are fetched once per sync cycle and diffed
/// against the mirror.</summary>
public sealed class SyncContainersResponse
{
    public required IReadOnlyList<AddressBookDto> AddressBooks { get; set; }
    public required IReadOnlyList<ContactGroupDto> Groups { get; set; }
}
