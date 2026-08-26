namespace LupiraContactApi.Core.Dtos.Internal;

/// <summary>Request/response of the service-to-service contact resolve seam (cal-api's IContactResolver).</summary>
public sealed class ResolveContactsRequest
{
    public required List<Guid> ContactIds { get; set; }
}
