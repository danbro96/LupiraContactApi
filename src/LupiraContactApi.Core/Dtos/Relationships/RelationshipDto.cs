using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Relationships;

/// <summary>A relationship between two contacts: "<c>HighId</c> is <c>LowId</c>'s <c>Kind</c>". Low/High is the ids' ordinal
/// order, not which side stated it. Each side has its own label (its word for the other); the rest is shared.</summary>
public sealed class RelationshipDto
{
    public required Guid Id { get; set; }

    public required Guid LowId { get; set; }

    public required Guid HighId { get; set; }

    public required ContactRelationKind Kind { get; set; }

    public required string? LabelFromLow { get; set; }

    public required string? LabelFromHigh { get; set; }

    public required DateOnly? Since { get; set; }

    public required string? Note { get; set; }

    public required bool Ended { get; set; }

    public required DateOnly? Until { get; set; }
}
