using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>A personal grouping (Friends/Family/Colleagues) vs a company/institution. An employer is membership in an <c>Organization</c>-kind group.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactGroupKind>))]
public enum ContactGroupKind { Group, Organization }
