using Marten;

namespace LupiraContactApi.Core.Data;

public static class EventLog
{
    /// <summary>The store's latest global event sequence (0 when empty).</summary>
    public static async Task<long> LatestSequenceAsync(this IQuerySession session, CancellationToken ct = default)
    {
        var last = await session.Events.QueryAllRawEvents().OrderByDescending(e => e.Sequence).Take(1).ToListAsync(ct);
        return last.Count > 0 ? last[0].Sequence : 0L;
    }
}
