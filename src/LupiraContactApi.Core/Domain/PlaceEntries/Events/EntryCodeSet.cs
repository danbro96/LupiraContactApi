namespace LupiraContactApi.Core.Domain.PlaceEntries.Events;

/// <summary>Adds or replaces the entry code <paramref name="CodeId"/> at a place.</summary>
public sealed record EntryCodeSet(Guid PlaceId, Guid CodeId, string Label, string Code, string? Note);
