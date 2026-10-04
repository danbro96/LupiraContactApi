namespace LupiraContactApi.Core.Domain.Residencies.Events;

/// <summary>The residency was entered by mistake and is erased. The snapshot stays as a tombstone so sync feeds can report it.</summary>
public sealed record ResidencyRemoved(Guid ResidencyId);
