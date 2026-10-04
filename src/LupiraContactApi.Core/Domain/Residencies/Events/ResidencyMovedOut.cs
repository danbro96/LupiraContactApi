using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Residencies.Events;

public sealed record ResidencyMovedOut(Guid ResidencyId, FuzzyDate MovedOut);
