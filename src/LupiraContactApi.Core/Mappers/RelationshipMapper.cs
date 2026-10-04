using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Dtos.Relationships;

namespace LupiraContactApi.Core.Mappers;

internal static class RelationshipMapper
{
    public static RelationshipDto ToResponse(this Relationship r) => new()
    {
        Id = r.Id,
        LowId = r.Low,
        HighId = r.High,
        Kind = r.Kind,
        LabelFromLow = r.LabelFromLow,
        LabelFromHigh = r.LabelFromHigh,
        Since = r.Since,
        Note = r.Note,
        Ended = r.Ended,
        Until = r.Ended ? r.Until : null,
    };
}
