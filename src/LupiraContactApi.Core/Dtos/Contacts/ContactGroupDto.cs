using System.Text.Json.Serialization;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>A contact group (personal grouping or organization) and its current members.</summary>
public sealed class ContactGroupDto
{
    public required Guid Id { get; set; }

    public required Guid AddressBookId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ContactGroupKind>))]
    public required ContactGroupKind Kind { get; set; }

    public required string Name { get; set; }

    public required IReadOnlyList<GroupMemberDto> Members { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
