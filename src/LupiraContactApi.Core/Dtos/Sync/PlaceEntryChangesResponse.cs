using LupiraContactApi.Core.Dtos.PlaceEntries;

namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>Entry codes changed past the cursor, per place. <c>Cursor</c> is opaque — hand it back as <c>?since=</c>. A full
/// sync (no <c>since</c>, or a <c>Reset</c>) lists every visible place and suppresses tombstones.</summary>
public sealed class PlaceEntryChangesResponse
{
    public required string Cursor { get; set; }

    public required bool Reset { get; set; }

    public required IReadOnlyList<PlaceEntryDto> Changed { get; set; }

    /// <summary>Place ids whose codes are no longer visible (no current resident the caller can read).</summary>
    public required IReadOnlyList<Guid> Deleted { get; set; }
}
