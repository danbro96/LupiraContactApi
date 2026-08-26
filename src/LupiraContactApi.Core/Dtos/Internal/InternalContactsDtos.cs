namespace LupiraContactApi.Dtos.Internal;

/// <summary>Request/response of the service-to-service contact resolve seam (cal-api's IContactResolver).</summary>
public sealed class ResolveContactsRequest
{
    public required List<Guid> ContactIds { get; set; }
}

public sealed class ContactSummaryDto
{
    public required Guid ContactId { get; set; }
    public required string DisplayName { get; set; }
}

public sealed class ResolveContactsResponse
{
    public required List<ContactSummaryDto> Contacts { get; set; }
}

/// <summary>A contact's birthday for the cal-api Birthdays projection. <see cref="Year"/> is null when only the
/// month-day is known (see <see cref="Domain.PartialDate"/>).</summary>
public sealed class ContactBirthdayDto
{
    public required Guid ContactId { get; set; }
    public required string DisplayName { get; set; }
    public int? Year { get; set; }
    public required int Month { get; set; }
    public required int Day { get; set; }
}

public sealed class ContactBirthdaysResponse
{
    public required List<ContactBirthdayDto> Contacts { get; set; }
}

public sealed class CheckPlaceReferencesRequest
{
    public required List<Guid> PlaceIds { get; set; }
}

public sealed class ContactPlaceRefDto
{
    public required Guid PlaceId { get; set; }
    public required int Count { get; set; }
}

public sealed class ContactPlaceReferencesResponse
{
    public required List<ContactPlaceRefDto> Places { get; set; }
}
