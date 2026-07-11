using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using System.Text.Json.Nodes;

namespace LupiraContactApi.Mappers;

/// <summary>Maps the <see cref="Contact"/> snapshot to its response DTO (display name is composed from the parts).
/// <paramref name="completeness"/> is computed by the service (organisation/role lives on a separate ContactGroup).</summary>
internal static class ContactMapper
{
    public static ContactDto ToResponse(this Contact c, CompletenessScore? completeness) => new()
    {
        Id = c.Id,
        AddressBookId = c.AddressBookId,
        ExternalId = c.ExternalId,
        DisplayName = c.DisplayName,
        GivenName = c.GivenName,
        FamilyName = c.FamilyName,
        Nickname = c.Nickname,
        Channels = c.Channels,
        Birthday = c.Birthday,
        Deceased = c.Deceased,
        DeathDate = c.DeathDate,
        Tags = c.Tags,
        Addresses = c.Addresses,
        Profiles = c.Profiles,
        EmergencyContactIds = c.EmergencyContactIds,
        Relations = c.Relations,
        Metadata = JsonNode.Parse(string.IsNullOrWhiteSpace(c.Metadata) ? "{}" : c.Metadata),
        Completeness = completeness,
        CreatedAt = c.CreatedAt,
        CreatedBy = c.CreatedBy,
        UpdatedAt = c.UpdatedAt,
        UpdatedBy = c.UpdatedBy,
        Etag = c.ContentHash,
    };
}
