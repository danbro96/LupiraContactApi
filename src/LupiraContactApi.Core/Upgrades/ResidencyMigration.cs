using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Shared;
using Marten;
using Marten.Events;

namespace LupiraContactApi.Core.Upgrades;

/// <summary>One-shot move of the legacy per-contact address lists onto <see cref="Residency"/> streams (current state only —
/// the old events stay in the contact streams as history). Idempotent: a residency whose stream exists is skipped.</summary>
public sealed class ResidencyMigration(IDocumentStore store)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        await using var session = store.LightweightSession();
        var legacy = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypesAre(typeof(ContactCreated), typeof(ContactImported), typeof(ContactRestored), typeof(ContactDeleted), typeof(ContactAddressesReplaced)))
            .ToListAsync(ct);

        session.SetHeader(EventActor.HeaderKey, "migration");
        var started = 0;
        foreach (var (streamId, events) in LegacyAddressFold.Residencies(LegacyAddressFold.Addresses(legacy)))
        {
            if (await session.Events.FetchStreamStateAsync(streamId, ct) is not null) continue;
            session.Events.StartStream<Residency>(streamId, events);
            started++;
        }

        await session.SaveChangesAsync(ct);
        return started;
    }
}
