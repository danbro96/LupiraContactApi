using Lupira.Results;
using Lupira.Sync;
using Lupira.Sync.Marten;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Mappers;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// Residencies for clients: those whose contact is live and readable by the caller. A delta also re-sends the
/// residencies of every contact changed since the cursor, because a contact's deletion or move changes what is visible
/// without touching the residency.
/// </summary>
public sealed class ResidencyFeed(IQuerySession session, AccessResolver access, IOptions<SyncFeedOptions> options)
{
    private readonly TimeSpan _settleLag = options.Value.SettleLag;

    public async Task<IReadOnlyList<ResidencyDto>> VisibleAsync(Guid principalId, CancellationToken ct = default)
    {
        var visibleContacts = await session.LiveContactIdsInAsync(await access.AccessibleAddressBookIdsAsync(principalId, ct), ct);
        return [.. (await session.LiveResidenciesAsync(ct)).Where(r => visibleContacts.Contains(r.ContactId)).Select(r => r.ToResponse())];
    }

    public async Task<OpResult<SyncPage<ResidencyDto>>> ChangesAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        if (!SyncFeedQuery.TryParse(since, limit, out var query))
            return OpResult<SyncPage<ResidencyDto>>.Invalid(SyncFeedQuery.InvalidSince);

        var readable = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var scope = SyncCursor.ScopeOf(readable);
        var visibleContacts = await session.LiveContactIdsInAsync(readable, ct);
        return OpResult<SyncPage<ResidencyDto>>.Ok(query.IsFullSync(scope)
            ? await FullSyncAsync(query, scope, visibleContacts, ct)
            : await DeltaAsync(query, scope, readable, visibleContacts, ct));
    }

    private async Task<SyncPage<ResidencyDto>> FullSyncAsync(SyncFeedQuery query, string scope, HashSet<Guid> visibleContacts, CancellationToken ct)
    {
        var (head, after) = await session.FullSyncFromAsync(query, scope, _settleLag, ct);
        var ids = visibleContacts.ToArray();
        var visible = session.Query<Residency>().Where(r => !r.Removed && ids.Contains(r.ContactId));
        if (after is { } last) visible = visible.Where(r => r.Id > last);
        var rows = (await visible.OrderBy(r => r.Id).Take(query.Limit + 1).ToListAsync(ct)).ToList();
        var resumeAfter = SyncPages.TrimToPage(rows, query.Limit, r => r.Id);
        return SyncPages.Full([.. rows.Select(r => r.ToResponse())], head, scope, query.IsReset(scope), resumeAfter);
    }

    private async Task<SyncPage<ResidencyDto>> DeltaAsync(SyncFeedQuery query, string scope, List<Guid> readable, HashSet<Guid> visibleContacts, CancellationToken ct)
    {
        var since = query.Since!.Value.Sequence;
        var head = await session.HeadSequenceAsync(_settleLag, ct);
        var own = await session.ChangedStreamsAsync<Residency>(since, query.Limit, _settleLag, ct);
        var contacts = await session.ChangedStreamsAsync<Contact>(since, query.Limit, _settleLag, ct);

        var rows = (await session.LoadManyAsync<Residency>(ct, own.Ids))
            .Concat(await session.ResidenciesOfAsync(contacts.Ids, ct))
            .DistinctBy(r => r.Id)
            .ToList();
        var known = await session.EverReadableContactIdsAsync(rows.Select(r => r.ContactId), readable, ct);
        rows = [.. rows.Where(r => known.Contains(r.ContactId))];
        var changed = rows.Where(r => r.IsLive && visibleContacts.Contains(r.ContactId)).ToList();
        return SyncPages.Delta([.. changed.Select(r => r.ToResponse())], [.. rows.Except(changed).Select(r => r.Id)], scope, head, own, contacts);
    }
}
