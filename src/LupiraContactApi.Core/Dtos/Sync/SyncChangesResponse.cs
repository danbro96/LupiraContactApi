namespace LupiraContactApi.Core.Dtos.Sync;

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
