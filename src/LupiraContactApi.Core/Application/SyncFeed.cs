using Lupira.Results;
using Lupira.Sync;
using Lupira.Sync.Marten;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Core.Mappers;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraContactApi.Core.Application;

/// <summary>
/// The offline-client contacts feed: live contacts in an address book the caller can read. A full sync pages them by id
/// up to the head sequence; a delta returns the contact streams changed since the cursor, with deleted ones and ones
/// moved out of a readable book as tombstones. An access change restarts the stream (<see cref="SyncCursor"/>).
/// </summary>
public sealed class SyncFeed(IQuerySession session, AccessResolver access, CompletenessResolver completeness, IOptions<SyncFeedOptions> options)
{
    private readonly TimeSpan _settleLag = options.Value.SettleLag;

    public async Task<OpResult<SyncPage<ContactSyncChange>>> ContactsAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        if (!SyncFeedQuery.TryParse(since, limit, out var query))
            return OpResult<SyncPage<ContactSyncChange>>.Invalid(SyncFeedQuery.InvalidSince);

        var readable = (await access.AccessibleAddressBookIdsAsync(principalId, ct)).ToArray();
        var scope = SyncCursor.ScopeOf(readable);
        return OpResult<SyncPage<ContactSyncChange>>.Ok(query.IsFullSync(scope)
            ? await FullSyncAsync(query, readable, scope, ct)
            : await DeltaAsync(query, readable, scope, ct));
    }

    private async Task<SyncPage<ContactSyncChange>> FullSyncAsync(SyncFeedQuery query, Guid[] readable, string scope, CancellationToken ct)
    {
        var (head, after) = await session.FullSyncFromAsync(query, scope, _settleLag, ct);
        var visible = session.Query<Contact>().Where(c => c.DeletedAt == null && readable.Contains(c.AddressBookId));
        if (after is { } last) visible = visible.Where(c => c.Id > last);
        var rows = (await visible.OrderBy(c => c.Id).Take(query.Limit + 1).ToListAsync(ct)).ToList();
        var resumeAfter = SyncPages.TrimToPage(rows, query.Limit, c => c.Id);
        return SyncPages.Full(await ToChangesAsync(rows, ct), head, scope, query.IsReset(scope), resumeAfter);
    }

    private async Task<SyncPage<ContactSyncChange>> DeltaAsync(SyncFeedQuery query, Guid[] readable, string scope, CancellationToken ct)
    {
        var changes = await session.ChangedStreamsAsync<Contact>(query.Since!.Value.Sequence, query.Limit, _settleLag, ct);

        // A moved contact still matches via the book it left, so that book's readers get the tombstone.
        var candidates = await session.Query<Contact>()
            .Where(c => changes.Ids.Contains(c.Id)
                && (readable.Contains(c.AddressBookId) || c.FormerAddressBookIds.Any(b => readable.Contains(b))))
            .ToListAsync(ct);

        var changed = candidates.Where(c => IsVisible(c, readable)).ToList();
        var deleted = candidates.Where(c => !IsVisible(c, readable)).Select(c => c.Id).ToList();
        return SyncPages.Delta(await ToChangesAsync(changed, ct), deleted, scope, changes);
    }

    private async Task<List<ContactSyncChange>> ToChangesAsync(List<Contact> contacts, CancellationToken ct)
    {
        var scores = await completeness.ScoreContactsAsync(contacts, ct);
        return [.. contacts.Select(c => new ContactSyncChange { Contact = c.ToResponse(scores[c.Id]), Guards = SectionGuardsDto.From(c) })];
    }

    private static bool IsVisible(Contact c, Guid[] readable) => c.DeletedAt is null && readable.Contains(c.AddressBookId);
}
