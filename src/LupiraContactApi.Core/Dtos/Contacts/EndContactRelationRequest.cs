using LupiraContactApi.Core.Domain;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Ends a relation (relationship ran its course — the edge stays, flagged). Removal is for edges entered by mistake.</summary>
public sealed class EndContactRelationRequest
{
    public required ContactRelationKind Kind { get; set; }
    public DateOnly? Until { get; set; }
}
