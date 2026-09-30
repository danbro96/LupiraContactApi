using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
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
    /// comms' contact directory — same ACL-free posture as resolve. Relation lines merge both sides' copies, so a
    /// relationship reads the same whichever contact stores it; ended relationships and unresolvable others are omitted.</summary>
    public async Task<Results<Ok<DescribeContactsResponse>, BadRequest<string>>> DescribeAsync(DescribeContactsRequest body, CancellationToken ct)
    {
        if (body.ContactIds.Count > MaxIds) return TypedResults.BadRequest($"At most {MaxIds} ids per request.");
        var ids = body.ContactIds.Distinct().ToList();
        var found = await session.Query<Contact>().Where(c => ids.Contains(c.Id) && c.DeletedAt == null).ToListAsync(ct);
        var holders = await session.Query<Contact>()
            .Where(c => c.DeletedAt == null && c.Relations.Any(r => ids.Contains(r.ToContactId))).ToListAsync(ct);

        var copies = found.Concat(holders).DistinctBy(c => c.Id)
            .SelectMany(c => c.Relations.Select(e => new RelationCopy(c.Id, e))).ToList();
        var resolved = found.ToDictionary(c => c.Id, c => RelationResolver.Resolve(c.Id, copies).Where(v => !v.Ended).ToList());
        var names = holders.ToDictionary(c => c.Id, c => c.DisplayName);
        var targetIds = resolved.Values.SelectMany(vs => vs.Select(v => v.OtherId)).Where(x => !names.ContainsKey(x)).Distinct().ToList();
        if (targetIds.Count > 0)
        {
            foreach (var t in await session.Query<Contact>().Where(c => targetIds.Contains(c.Id) && c.DeletedAt == null).ToListAsync(ct))
                names[t.Id] = t.DisplayName;
        }

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
