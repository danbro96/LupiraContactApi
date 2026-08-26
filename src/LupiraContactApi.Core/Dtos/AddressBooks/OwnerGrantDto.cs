using System.Text.Json.Serialization;
using LupiraContactApi.Core.Domain;

namespace LupiraContactApi.Core.Dtos.AddressBooks;

/// <summary>The result of granting a member access to a container: who now has what access on which container.</summary>
public sealed class OwnerGrantDto
{
    public required Guid ContainerId { get; set; }
    public required Guid PrincipalId { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<Access>))]
    public required Access Access { get; set; }
}
