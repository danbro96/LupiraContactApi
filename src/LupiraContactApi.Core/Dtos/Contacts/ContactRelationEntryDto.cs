using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>One relationship as seen from the viewed contact, identical whichever side stores it. <see cref="Kind"/> is the OTHER
/// contact's role relative to the viewed one and <see cref="Label"/> the viewed contact's own name for them; since, note and the
/// ended flag belong to the relationship and read the same from both sides. <see cref="Provenance"/> distinguishes stored
/// relationships from kin derived off the parent/child graph (returned only when inferred relations are requested).</summary>
public sealed class ContactRelationEntryDto
{
    public required Guid ContactId { get; set; }

    public required string DisplayName { get; set; }

    public required ContactRelationKind Kind { get; set; }

    public string? Label { get; set; }

    /// <summary>When the relationship began, if a precise date is known.</summary>
    public DateOnly? Since { get; set; }

    /// <summary>Free-text note about the relationship.</summary>
    public string? Note { get; set; }

    public RelationProvenance Provenance { get; set; } = RelationProvenance.Explicit;

    /// <summary>The relationship ran its course (ex-spouse); it remains for history but asserts no current kinship.</summary>
    public bool Ended { get; set; }

    public DateOnly? Until { get; set; }
}
