using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Residencies;

/// <summary>A residency as stated: start one, or correct one wholesale.</summary>
public sealed class ResidencyRequest
{
    /// <summary>A LupiraGeoApi place id — resolve the address there first; no free text.</summary>
    public required Guid PlaceId { get; set; }

    public required ContactAddressType Type { get; set; }

    /// <summary>Free-text refinement, e.g. "Summer house, Gotland".</summary>
    public string? Label { get; set; }

    public FuzzyDate? MovedIn { get; set; }

    public FuzzyDate? MovedOut { get; set; }
}
