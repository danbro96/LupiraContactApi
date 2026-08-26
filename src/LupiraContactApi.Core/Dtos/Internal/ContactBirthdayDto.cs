namespace LupiraContactApi.Core.Dtos.Internal;

/// <summary>A contact's birthday for the cal-api Birthdays projection. <see cref="Year"/> is null when only the
/// month-day is known (see <see cref="Domain.Shared.PartialDate"/>).</summary>
public sealed class ContactBirthdayDto
{
    public required Guid ContactId { get; set; }

    public required string DisplayName { get; set; }

    public int? Year { get; set; }

    public required int Month { get; set; }

    public required int Day { get; set; }
}
