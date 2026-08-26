namespace LupiraContactApi.Core.Dtos.Internal;

public sealed class ContactPlaceRefDto
{
    public required Guid PlaceId { get; set; }

    public required int Count { get; set; }
}
