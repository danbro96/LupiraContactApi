using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Residencies;

/// <summary>Several contacts move together, e.g. a family: each one's current residencies at <c>FromPlaceId</c> end on
/// <c>MovedIn</c>, and a new residency at <c>ToPlaceId</c> starts then.</summary>
public sealed class MoveRequest
{
    public required IReadOnlyList<Guid> ContactIds { get; set; }

    public required Guid ToPlaceId { get; set; }

    public required ContactAddressType Type { get; set; }

    public string? Label { get; set; }

    public required FuzzyDate MovedIn { get; set; }

    /// <summary>The place they leave; omit when nobody leaves anywhere (a new vacation home, say).</summary>
    public Guid? FromPlaceId { get; set; }
}
