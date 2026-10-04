namespace LupiraContactApi.Core.Dtos.PlaceEntries;

public sealed class SetEntryCodeRequest
{
    /// <summary>What it opens, e.g. "Port" or "Gate".</summary>
    public required string Label { get; set; }

    public required string Code { get; set; }

    /// <summary>Optional hint, e.g. "ring 2nd floor".</summary>
    public string? Note { get; set; }
}
