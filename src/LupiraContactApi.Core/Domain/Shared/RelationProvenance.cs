using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>Whether a resolved relation was stored explicitly or derived from the kinship graph.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RelationProvenance>))]
public enum RelationProvenance
{
    Explicit,
    Inferred,
}
