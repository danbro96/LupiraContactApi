using Lupira.Sync;
using Lupira.Sync.Marten;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>The shared <c>/sync/*</c> feed algorithm's paging: a full sync pages visible rows by id up to the head
/// read before it, a delta resumes after the changed streams it returned, and a snapshot is always a complete reset.</summary>
public static class SyncPages
{
    /// <summary>Head and resume id of a full-sync page. A reset reads the settled head first, so anything written
    /// during the full sync arrives in the next delta.</summary>
    public static async Task<(long Head, Guid? After)> FullSyncFromAsync(this IQuerySession session, SyncFeedQuery query, string scope, TimeSpan settleLag, CancellationToken ct) =>
        query.IsReset(scope) ? (await session.HeadSequenceAsync(settleLag, ct), null) : (query.Since!.Value.Sequence, query.Since.Value.After);

    /// <summary>Drops the probe row read past <paramref name="limit"/>; the id to resume after when there was one.</summary>
    public static Guid? TrimToPage<T>(List<T> rows, int limit, Func<T, Guid> idOf)
    {
        if (rows.Count <= limit) return null;
        rows.RemoveRange(limit, rows.Count - limit);
        return idOf(rows[^1]);
    }

    public static SyncPage<T> Full<T>(List<T> changed, long head, string scope, bool reset, Guid? resumeAfter) =>
        Page(new SyncCursor(head, scope) { After = resumeAfter }, resumeAfter is not null, reset, changed, []);

    public static SyncPage<T> Delta<T>(List<T> changed, List<Guid> deleted, string scope, ChangedStreams changes) =>
        Page(new SyncCursor(changes.NextSequence, scope), changes.HasMore, false, changed, deleted);

    /// <summary>A delta over several change sources read after <paramref name="head"/>: resumes at the lowest source
    /// still holding more, else at the head they all cover.</summary>
    public static SyncPage<T> Delta<T>(List<T> changed, List<Guid> deleted, string scope, long head, params ChangedStreams[] sources)
    {
        var pending = sources.Where(s => s.HasMore).Select(s => s.NextSequence).ToList();
        var next = pending.Count > 0 ? Math.Min(head, pending.Min()) : head;
        return Page(new SyncCursor(next, scope), pending.Count > 0, false, changed, deleted);
    }

    public static SyncPage<T> Snapshot<T>(List<T> all) => new() { Cursor = string.Empty, HasMore = false, Reset = true, Changed = all, Deleted = [] };

    private static SyncPage<T> Page<T>(SyncCursor cursor, bool hasMore, bool reset, List<T> changed, List<Guid> deleted) =>
        new() { Cursor = cursor.ToString(), HasMore = hasMore, Reset = reset, Changed = changed, Deleted = deleted };
}
