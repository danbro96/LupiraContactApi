namespace LupiraContactApi.Core.Domain.PlaceEntries.Events;

public sealed record EntryCodeRemoved(Guid PlaceId, Guid CodeId);
