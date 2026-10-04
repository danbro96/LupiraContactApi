namespace LupiraContactApi.Core.Domain.PlaceEntries;

/// <summary>One way in: a door or gate code with what it opens ("Port", "Gate") and an optional hint ("ring 2nd floor").</summary>
public sealed class EntryCode
{
    public Guid Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Note { get; set; }
}
