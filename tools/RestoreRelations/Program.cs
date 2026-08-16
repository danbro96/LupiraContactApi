using JasperFx.Events;
using LupiraContactApi.Data;
using LupiraContactApi.Domain;
using Marten;

// One-off repair for relations and emergency designations erased by the pre-fix DAV write path, which
// treated absent RELATED lines as a wholesale clear. Recovers the state from just before each empty
// replace and merges it back; additive, so re-running is safe. Dry run unless --apply is passed.

var apply = args.Contains("--apply");
var connection = Environment.GetEnvironmentVariable("CONTACT_DB")
    ?? throw new InvalidOperationException("Set CONTACT_DB to the LupiraContactApi Postgres connection string.");

const string RestoreActor = "restore-relations";

await using var store = DocumentStore.For(opts =>
{
    opts.Connection(connection);
    opts.UseLupiraContact();
});

await using var session = store.LightweightSession();
session.SetHeader(EventActor.HeaderKey, RestoreActor);

var wipes = await session.Events.QueryAllRawEvents()
    .Where(e => e.EventTypeName == "contact_relations_replaced" || e.EventTypeName == "contact_emergency_contacts_replaced")
    .OrderBy(e => e.Sequence)
    .ToListAsync();

var relationWipes = wipes.Where(e => e.Data is ContactRelationsReplaced { Relations.Count: 0 });
var emergencyWipes = wipes.Where(e => e.Data is ContactEmergencyContactsReplaced { ContactIds.Count: 0 });

var lostRelations = await RecoverAsync(relationWipes, c => c.Relations.Count > 0, c => c.Relations);
var lostEmergency = await RecoverAsync(emergencyWipes, c => c.EmergencyContactIds.Count > 0, c => c.EmergencyContactIds);

var touched = 0;
foreach (var id in lostRelations.Keys.Union(lostEmergency.Keys).OrderBy(x => x))
{
    var current = await session.Events.AggregateStreamAsync<Contact>(id);
    if (current is null) continue;

    // Union, not overwrite: edges added after the wipe are real and must survive the repair.
    var relations = current.Relations
        .Concat(lostRelations.GetValueOrDefault(id, []).Where(r => !current.Relations.Any(x => x.ToContactId == r.ToContactId && x.Kind == r.Kind)))
        .ToList();
    var emergency = current.EmergencyContactIds
        .Concat(lostEmergency.GetValueOrDefault(id, []).Where(x => !current.EmergencyContactIds.Contains(x)))
        .ToList();

    var addedRelations = relations.Count - current.Relations.Count;
    var addedEmergency = emergency.Count - current.EmergencyContactIds.Count;
    if (addedRelations == 0 && addedEmergency == 0) continue;

    Console.WriteLine($"{id}  {current.DisplayName,-32} +{addedRelations} relation(s)  +{addedEmergency} emergency");
    foreach (var r in relations.Skip(current.Relations.Count))
        Console.WriteLine($"    {r.Kind,-12} -> {r.ToContactId}{(r.Ended ? "  (ended)" : "")}{(r.Label is null ? "" : $"  \"{r.Label}\"")}");
    touched++;

    if (!apply) continue;
    if (addedRelations > 0) session.Events.Append(id, new ContactRelationsReplaced(id, relations));
    if (addedEmergency > 0) session.Events.Append(id, new ContactEmergencyContactsReplaced(id, emergency));
}

if (apply) await session.SaveChangesAsync();
Console.WriteLine($"\n{touched} contact(s) {(apply ? "restored" : "would be restored — re-run with --apply")}.");

// Aggregates each wiped stream to the version before the wipe. Keeps the earliest recoverable state per
// stream: a later wipe's "before" is already empty, so first-wins is what actually holds the lost edges.
async Task<Dictionary<Guid, List<T>>> RecoverAsync<T>(IEnumerable<IEvent> events, Func<Contact, bool> hasContent, Func<Contact, List<T>> select)
{
    var recovered = new Dictionary<Guid, List<T>>();
    foreach (var e in events)
    {
        if (recovered.ContainsKey(e.StreamId)) continue;
        var before = await session.Events.AggregateStreamAsync<Contact>(e.StreamId, version: e.Version - 1);
        if (before is not null && hasContent(before)) recovered[e.StreamId] = select(before);
    }
    return recovered;
}
