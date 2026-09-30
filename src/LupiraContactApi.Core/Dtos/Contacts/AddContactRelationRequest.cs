using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Upserts a relationship from either side: "<see cref="ToContactId"/> is this contact's <see cref="Kind"/>".</summary>
public sealed class AddContactRelationRequest
{
    public required Guid ToContactId { get; set; }

    public required ContactRelationKind Kind { get; set; }

    /// <summary>This contact's own name for the other, e.g. "dad". The other side keeps its own.</summary>
    public string? Label { get; set; }

    /// <summary>When the relationship began, if a precise date is known.</summary>
    public DateOnly? Since { get; set; }

    /// <summary>Free-text note about the relationship (how/where it started); fuzzy periods that aren't a precise date go here.</summary>
    public string? Note { get; set; }
}
