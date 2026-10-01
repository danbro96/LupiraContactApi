using System.Text.Json.Serialization;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.AddressBooks;

/// <summary>An address book the caller can access, with their access level.</summary>
public sealed class AddressBookDto
{
    public required Guid Id { get; set; }

    public required string Slug { get; set; }

    public string? DisplayName { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<Access>))]
    public required Access Access { get; set; }

    /// <summary>True only for the caller's own personal book — another member's shared personal book has the same slug.</summary>
    public required bool IsPersonal { get; set; }
}
