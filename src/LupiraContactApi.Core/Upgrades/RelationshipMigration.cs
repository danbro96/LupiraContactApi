using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;
using Marten;
using Marten.Events;

namespace LupiraContactApi.Core.Upgrades;

/// <summary>One-shot move of the legacy per-contact relation copies onto <see cref="Relationship"/> streams (current state
/// only — the old events stay in the contact streams as history). Idempotent: a relationship whose stream exists is skipped.</summary>
public sealed class RelationshipMigration(IDocumentStore store)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        await using var session = store.LightweightSession();
        var legacy = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypesAre(typeof(ContactRelationAdded), typeof(ContactRelationEnded), typeof(ContactRelationRemoved), typeof(ContactRelationsReplaced)))
            .ToListAsync(ct);

        session.SetHeader(EventActor.HeaderKey, "migration");
        var started = 0;
        foreach (var (streamId, events) in LegacyRelationFold.Relationships(LegacyRelationFold.Copies(legacy)))
        {
            if (await session.Events.FetchStreamStateAsync(streamId, ct) is not null) continue;
            session.Events.StartStream<Relationship>(streamId, events);
            started++;
        }

        await session.SaveChangesAsync(ct);
        return started;
    }
}
