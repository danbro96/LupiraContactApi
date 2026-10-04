using JasperFx.Events;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Upgrades;

/// <summary>Turns the legacy per-contact address lists into residencies. Pure: replays the old events exactly as the contact
/// snapshot applied them — last writer wins per (occurredAt, commandId), and a deleted contact ignores address writes —
/// then starts one residency per surviving entry.</summary>
public static class LegacyAddressFold
{
    public static IReadOnlyDictionary<Guid, List<ContactPostalAddress>> Addresses(IEnumerable<IEvent> events)
    {
        var state = new Dictionary<Guid, (List<ContactPostalAddress> List, DateTimeOffset Ts, Guid Cmd, bool Deleted)>();
        foreach (var e in events.OrderBy(e => e.Sequence))
        {
            var s = state.TryGetValue(e.StreamId, out var found) ? found : ([], default, Guid.Empty, false);
            switch (e.Data)
            {
                case ContactCreated or ContactImported or ContactRestored:
                    s.Deleted = false;
                    break;
                case ContactDeleted:
                    s.Deleted = true;
                    break;
                case ContactAddressesReplaced d:
                    var (ts, cmd) = (d.OccurredAt ?? e.Timestamp, d.CommandId ?? SectionLww.FromSequence(e.Sequence));
                    if (!s.Deleted && SectionLww.Wins(ts, cmd, s.Ts, s.Cmd)) s = ([.. d.Addresses], ts, cmd, false);
                    break;
            }

            state[e.StreamId] = s;
        }

        return state.Where(x => x.Value.List.Count > 0).ToDictionary(x => x.Key, x => x.Value.List);
    }

    /// <summary>One residency per address, its stream id fixed by contact and position so a re-run lands on the same streams.</summary>
    public static IReadOnlyList<(Guid StreamId, IReadOnlyList<object> Events)> Residencies(IReadOnlyDictionary<Guid, List<ContactPostalAddress>> addresses) =>
        [.. addresses.SelectMany(c => c.Value.Select((a, i) =>
        {
            var id = DeterministicGuid.From($"residency:{c.Key:D}:{i}");
            return (id, Residency.Start(id, c.Key, a.PlaceId, a.Type, null, a.MovedIn, a.MovedOut));
        }))];
}
