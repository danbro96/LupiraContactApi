using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Residencies;

public sealed class MoveOutRequest
{
    public required FuzzyDate MovedOut { get; set; }
}
