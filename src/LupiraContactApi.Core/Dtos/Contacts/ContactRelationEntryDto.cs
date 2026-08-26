using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>One resolved relation as seen from the viewed contact: <see cref="Kind"/> is always the OTHER contact's role
/// relative to the viewed one (incoming edges show the derived inverse kind, and their label — the other side's phrasing — is omitted).
/// <see cref="Provenance"/> distinguishes stored edges from kin derived off the parent/child graph (returned only when inferred relations are requested).</summary>
public sealed class ContactRelationEntryDto
{
    public required Guid ContactId { get; set; }

    public required string DisplayName { get; set; }

    public required ContactRelationKind Kind { get; set; }

    public string? Label { get; set; }

    /// <summary>When the relationship began, on outgoing edges where a precise date is known.</summary>
    public DateOnly? Since { get; set; }

    /// <summary>Free-text note about the edge, on outgoing edges.</summary>
    public string? Note { get; set; }

    public required ContactRelationDirection Direction { get; set; }

    public RelationProvenance Provenance { get; set; } = RelationProvenance.Explicit;

    /// <summary>The relationship ran its course (ex-spouse); the edge remains for history but asserts no current kinship.</summary>
    public bool Ended { get; set; }

    public DateOnly? Until { get; set; }
}
