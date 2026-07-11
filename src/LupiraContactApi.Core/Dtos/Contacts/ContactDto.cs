using LupiraContactApi.Domain;
using System.Text.Json.Nodes;

namespace LupiraContactApi.Dtos.Contacts;

public sealed class ContactDto
{
    public required Guid Id { get; set; }
    public required Guid AddressBookId { get; set; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? Nickname { get; set; }
    public required IReadOnlyList<ContactReachChannel> Channels { get; set; }
    public DateOnly? Birthday { get; set; }
    public bool Deceased { get; set; }
    public DateOnly? DeathDate { get; set; }
    public string[]? Tags { get; set; }
    public required IReadOnlyList<ContactPostalAddress> Addresses { get; set; }
    public required IReadOnlyList<ContactSocialProfile> Profiles { get; set; }

    /// <summary>Ordered designation (first = highest priority) — who to call about this person, not a kinship.</summary>
    public required IReadOnlyList<Guid> EmergencyContactIds { get; set; }

    /// <summary>Raw outgoing edges (unfiltered; targets may be deleted or unreadable). The <c>/relations</c> sub-resource is the resolved two-way view.</summary>
    public required IReadOnlyList<ContactRelation> Relations { get; set; }
    public JsonNode? Metadata { get; set; }

    /// <summary>How well-documented this contact is. Drives contact-enrichment ranking (completeness × relevance).</summary>
    public CompletenessScore? Completeness { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public required string Etag { get; set; }
}
