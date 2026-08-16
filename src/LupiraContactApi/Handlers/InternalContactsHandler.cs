using LupiraContactApi.Domain;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

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

/// <summary>Existence + display-name lookup for sibling services (deliberately ACL-free — the fence is the
/// <c>internal:read</c> service scope and the LAN-only edge). Unknown or deleted ids are simply absent
/// from the response.</summary>
public sealed class InternalContactsHandler(IQuerySession session)
{
    private const int MaxIds = 100;
    private const int MaxPlaceIds = 1000;

    public async Task<Results<Ok<ResolveContactsResponse>, BadRequest<string>>> ResolveAsync(ResolveContactsRequest body, CancellationToken ct)
    {
        if (body.ContactIds.Count > MaxIds) return TypedResults.BadRequest($"At most {MaxIds} ids per request.");
        var ids = body.ContactIds.Distinct().ToList();
        var found = await session.Query<Contact>().Where(c => ids.Contains(c.Id) && c.DeletedAt == null).ToListAsync(ct);
        return TypedResults.Ok(new ResolveContactsResponse
        {
            Contacts = [.. found.Select(c => new ContactSummaryDto { ContactId = c.Id, DisplayName = c.DisplayName })],
        });
    }

    /// <summary>Every live, non-deceased contact that carries a birthday — cal-api synthesizes the Birthdays
    /// calendar from this (year-less birthdays recur on month-day only). Same ACL-free posture as resolve;
    /// family-scale, so filtered in memory.</summary>
    public async Task<Ok<ContactBirthdaysResponse>> BirthdaysAsync(CancellationToken ct)
    {
        var live = await session.Query<Contact>().Where(c => c.DeletedAt == null && !c.Deceased).ToListAsync(ct);
        return TypedResults.Ok(new ContactBirthdaysResponse
        {
            Contacts = [.. live.Where(c => c.Birthday is not null).Select(c => new ContactBirthdayDto
            {
                ContactId = c.Id,
                DisplayName = c.DisplayName,
                Year = c.Birthday!.Year,
                Month = c.Birthday.Month,
                Day = c.Birthday.Day,
            })],
        });
    }

    /// <summary>How many address entries reference each of the requested geo place ids — geo's orphan sweep asks
    /// this before pruning. Deceased contacts and moved-out addresses still count (residency history anchors
    /// places); only deleted contacts don't. Zero-count ids are omitted.</summary>
    public async Task<Results<Ok<ContactPlaceReferencesResponse>, BadRequest<string>>> CheckPlaceReferencesAsync(
        CheckPlaceReferencesRequest body, CancellationToken ct)
    {
        if (body.PlaceIds.Count == 0 || body.PlaceIds.Count > MaxPlaceIds)
            return TypedResults.BadRequest($"Between 1 and {MaxPlaceIds} ids per request.");
        var requested = body.PlaceIds.ToHashSet();
        var live = await session.Query<Contact>().Where(c => c.DeletedAt == null).ToListAsync(ct);
        var counts = live.SelectMany(c => c.Addresses)
            .Where(a => requested.Contains(a.PlaceId))
            .GroupBy(a => a.PlaceId)
            .Select(g => new ContactPlaceRefDto { PlaceId = g.Key, Count = g.Count() });
        return TypedResults.Ok(new ContactPlaceReferencesResponse { Places = [.. counts] });
    }
}
