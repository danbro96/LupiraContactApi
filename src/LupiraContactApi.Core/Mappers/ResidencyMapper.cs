using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Dtos.Residencies;

namespace LupiraContactApi.Core.Mappers;

internal static class ResidencyMapper
{
    public static ResidencyDto ToResponse(this Residency r) => new()
    {
        Id = r.Id,
        ContactId = r.ContactId,
        PlaceId = r.PlaceId,
        Type = r.Type,
        Label = r.Label,
        MovedIn = r.MovedIn,
        MovedOut = r.MovedOut,
    };
}
