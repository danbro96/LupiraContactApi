using Lupira.Contracts.PlaceRefs;
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

    /// <summary>How many residencies reference each of the requested geo place ids — geo's orphan sweep asks this
    /// before pruning. Deceased contacts and moved-out residencies still count as live (residency history anchors
    /// places); deleted contacts' residencies count separately. Zero-count ids are omitted.</summary>
    public async Task<Results<Ok<PlaceReferencesResponse>, BadRequest<string>>> CheckPlaceReferencesAsync(
        CheckPlaceReferencesRequest body, CancellationToken ct)
    {
        if (body.PlaceIds.Count == 0 || body.PlaceIds.Count > MaxPlaceIds)
            return TypedResults.BadRequest($"Between 1 and {MaxPlaceIds} ids per request.");
        var residencies = await session.ResidenciesAtAsync([.. body.PlaceIds.Distinct()], ct);
        var contactIds = residencies.Select(r => r.ContactId).Distinct().ToArray();
        var deleted = (await session.LoadManyAsync<Contact>(ct, contactIds)).Where(c => c.DeletedAt is not null).Select(c => c.Id).ToHashSet();
        var counts = residencies
            .GroupBy(r => r.PlaceId)
            .Select(g => new PlaceReferenceCountDto
            {
                PlaceId = g.Key,
                LiveCount = g.Count(r => !deleted.Contains(r.ContactId)),
                DeletedCount = g.Count(r => deleted.Contains(r.ContactId)),
            });
        return TypedResults.Ok(new PlaceReferencesResponse { Places = [.. counts] });
    }
}
