namespace LupiraContactApi.Core.Dtos.PlaceEntries;

public sealed class EntryCodeDto
{
    public required Guid Id { get; set; }

    /// <summary>What it opens, e.g. "Port" or "Gate".</summary>
    public required string Label { get; set; }

    public required string Code { get; set; }

    public required string? Note { get; set; }
}
