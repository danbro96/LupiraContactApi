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

/// <summary>Existence + display-name lookup for sibling services (deliberately ACL-free — the fence is the
/// service audience and the LAN-only edge, matching geo's <c>places/resolve</c> posture). Unknown or
/// deleted ids are simply absent from the response.</summary>
public sealed class InternalContactsHandler(IQuerySession session)
{
    private const int MaxIds = 100;

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
}
