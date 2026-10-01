namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>One page of the changes feed. <c>Cursor</c> is opaque — hand it back as <c>?since=</c>; loop while
/// <c>HasMore</c>. A full sync (no <c>since</c>, or a <c>Reset</c>) streams every live visible contact and suppresses
/// tombstones — the client replaces its mirror wholesale.</summary>
public sealed class SyncChangesResponse
{
    public required string Cursor { get; set; }

    public required bool HasMore { get; set; }

    /// <summary>Stream restarted from zero (no <c>since</c>, or readable address books changed): a full sync from here.</summary>
    public required bool Reset { get; set; }

    public required IReadOnlyList<SyncChangeDto> Changed { get; set; }

    /// <summary>Ids in, or moved out of, a readable address book but no longer visible (deleted, or moved to an
    /// unreadable book). Unknown ids are safe to ignore.</summary>
    public required IReadOnlyList<Guid> Deleted { get; set; }
}
