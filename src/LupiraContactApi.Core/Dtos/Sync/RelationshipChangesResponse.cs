using LupiraContactApi.Core.Dtos.Relationships;

namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>Relationships changed past the cursor. <c>Cursor</c> is opaque — hand it back as <c>?since=</c>. A full sync
/// (no <c>since</c>, or a <c>Reset</c>) lists every visible relationship and suppresses tombstones — the client replaces its
/// mirror wholesale.</summary>
public sealed class RelationshipChangesResponse
{
    public required string Cursor { get; set; }

    /// <summary>Stream restarted (no <c>since</c>, or readable address books changed): a full sync from here.</summary>
    public required bool Reset { get; set; }

    public required IReadOnlyList<RelationshipDto> Changed { get; set; }

    /// <summary>Ids removed, or no longer visible (a contact deleted or moved out of reach). Unknown ids are safe to ignore.</summary>
    public required IReadOnlyList<Guid> Deleted { get; set; }
}
