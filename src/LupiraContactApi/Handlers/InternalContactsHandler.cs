using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Dtos.Internal;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

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

    /// <summary>Descriptor material (name, nickname, pronouns, tags, notes, rendered relation lines) for
    /// comms' contact directory — same ACL-free posture as resolve. Each relation line is the relationship as seen from the
    /// described contact (its own label, else the kind); ended relationships and unresolvable others are omitted.</summary>
    public async Task<Results<Ok<DescribeContactsResponse>, BadRequest<string>>> DescribeAsync(DescribeContactsRequest body, CancellationToken ct)
    {
        if (body.ContactIds.Count > MaxIds) return TypedResults.BadRequest($"At most {MaxIds} ids per request.");
        var ids = body.ContactIds.Distinct().ToList();
        var found = await session.Query<Contact>().Where(c => ids.Contains(c.Id) && c.DeletedAt == null).ToListAsync(ct);
        var relationships = (await session.RelationshipsOfAsync([.. found.Select(c => c.Id)], ct)).Where(r => !r.Ended).ToList();
        var resolved = found.ToDictionary(c => c.Id, c => relationships.Where(r => r.Involves(c.Id)).Select(r => r.ViewFrom(c.Id)).ToList());
        var otherIds = resolved.Values.SelectMany(vs => vs.Select(v => v.OtherId)).Distinct().ToList();
        var names = otherIds.Count == 0
            ? []
            : (await session.Query<Contact>().Where(c => otherIds.Contains(c.Id) && c.DeletedAt == null).ToListAsync(ct))
                .ToDictionary(c => c.Id, c => c.DisplayName);

        return TypedResults.Ok(new DescribeContactsResponse
        {
            Contacts = [.. found.Select(c => new ContactDescriptionDto
            {
                ContactId = c.Id,
                DisplayName = c.DisplayName,
                Nickname = c.Nickname,
                Pronouns = c.Pronouns,
                Tags = c.Tags ?? [],
                Notes = c.Notes,
                Relations = [.. resolved[c.Id]
                    .Where(v => names.ContainsKey(v.OtherId))
                    .Select(v => $"{v.Label ?? v.Kind.ToString().ToLowerInvariant()}: {names[v.OtherId]}")],
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
