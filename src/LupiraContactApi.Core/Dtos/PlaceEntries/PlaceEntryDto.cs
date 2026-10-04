namespace LupiraContactApi.Core.Dtos.PlaceEntries;

/// <summary>How to get in at a place: its door and gate codes, shared by everyone living there.</summary>
public sealed class PlaceEntryDto
{
    public required Guid PlaceId { get; set; }

    public required IReadOnlyList<EntryCodeDto> Codes { get; set; }
}
