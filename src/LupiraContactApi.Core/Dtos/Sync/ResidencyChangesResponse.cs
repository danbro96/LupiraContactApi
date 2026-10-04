using LupiraContactApi.Core.Dtos.Residencies;

namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>Residencies changed past the cursor. <c>Cursor</c> is opaque — hand it back as <c>?since=</c>. A full sync
/// (no <c>since</c>, or a <c>Reset</c>) lists every visible residency and suppresses tombstones.</summary>
public sealed class ResidencyChangesResponse
{
    public required string Cursor { get; set; }

    public required bool Reset { get; set; }

    public required IReadOnlyList<ResidencyDto> Changed { get; set; }

    /// <summary>Ids removed, or no longer visible (the contact deleted or moved out of reach). Unknown ids are safe to ignore.</summary>
    public required IReadOnlyList<Guid> Deleted { get; set; }
}
