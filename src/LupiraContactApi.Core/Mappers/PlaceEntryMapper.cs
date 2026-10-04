using LupiraContactApi.Core.Domain.PlaceEntries;
using LupiraContactApi.Core.Dtos.PlaceEntries;

namespace LupiraContactApi.Core.Mappers;

internal static class PlaceEntryMapper
{
    public static PlaceEntryDto ToResponse(this PlaceEntry e) => new()
    {
        PlaceId = e.PlaceId,
        Codes = [.. e.Codes.OrderBy(c => c.Label).Select(c => new EntryCodeDto { Id = c.Id, Label = c.Label, Code = c.Code, Note = c.Note })],
    };
}
