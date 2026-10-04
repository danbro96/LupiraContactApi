using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Residencies.Events;

public sealed record ResidencyStarted(
    Guid ResidencyId, Guid ContactId, Guid PlaceId, ContactAddressType Type, string? Label, FuzzyDate? MovedIn, FuzzyDate? MovedOut);
