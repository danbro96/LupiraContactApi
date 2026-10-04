using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Residencies.Events;

/// <summary>A correction of the residency as entered: place, type, label and period, replaced wholesale.</summary>
public sealed record ResidencyRevised(
    Guid ResidencyId, Guid PlaceId, ContactAddressType Type, string? Label, FuzzyDate? MovedIn, FuzzyDate? MovedOut);
