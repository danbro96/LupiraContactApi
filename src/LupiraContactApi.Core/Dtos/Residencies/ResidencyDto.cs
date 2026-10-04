using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Residencies;

/// <summary>A contact's tie to a geo place over a period. Current = today inside [movedIn, movedOut]; null ends are open.</summary>
public sealed class ResidencyDto
{
    public required Guid Id { get; set; }

    public required Guid ContactId { get; set; }

    public required Guid PlaceId { get; set; }

    public required ContactAddressType Type { get; set; }

    public required string? Label { get; set; }

    public required FuzzyDate? MovedIn { get; set; }

    public required FuzzyDate? MovedOut { get; set; }
}
