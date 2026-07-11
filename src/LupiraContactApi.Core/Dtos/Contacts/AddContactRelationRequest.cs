using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Upserts a directed relation edge on a contact: "<see cref="ToContactId"/> is this contact's <see cref="Kind"/>".</summary>
public sealed class AddContactRelationRequest
{
    public required Guid ToContactId { get; set; }
    public required ContactRelationKind Kind { get; set; }

    /// <summary>Free-text refinement of the kind, e.g. "dad".</summary>
    public string? Label { get; set; }

    /// <summary>When the relationship began, if a precise date is known.</summary>
    public DateOnly? Since { get; set; }

    /// <summary>Free-text note about the edge (how/where it started); fuzzy periods that aren't a precise date go here.</summary>
    public string? Note { get; set; }
}
