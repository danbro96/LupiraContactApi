namespace LupiraContactApi.Core.Dtos.Internal;

/// <summary>Request of the service-to-service contact describe seam (comms' contact directory).</summary>
public sealed class DescribeContactsRequest
{
    public required List<Guid> ContactIds { get; set; }
}
