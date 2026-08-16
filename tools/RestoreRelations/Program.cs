using JasperFx;
using JasperFx.Events;
using LupiraContactApi.Data;
using LupiraContactApi.Domain;
using Marten;

// One-off repair for relations and emergency designations erased by the pre-fix DAV write path, which
// treated absent RELATED lines as a wholesale clear. Recovers the state from just before each empty
// replace and merges it back; additive, so re-running is safe. Dry run unless --apply is passed.
//
// Folds only the relation, emergency and name events rather than aggregating whole streams: legacy
// contact_addresses_replaced payloads carry a null PlaceId that no longer deserializes, and a repair
// tool must not depend on unrelated history staying readable.

var apply = args.Contains("--apply");
var connection = Environment.GetEnvironmentVariable("CONTACT_DB")
    ?? throw new InvalidOperationException("Set CONTACT_DB to the LupiraContactApi Postgres connection string.");

const string RestoreActor = "restore-relations";

string[] eventTypes =
[
    "contact_relation_added", "contact_relation_removed", "contact_relation_ended", "contact_relations_replaced",
    "contact_emergency_contacts_replaced", "contact_created", "contact_imported", "contact_revised",
];

await using var store = DocumentStore.For(opts =>
{
    opts.Connection(connection);
    opts.UseLupiraContact();
    opts.AutoCreateSchemaObjects = AutoCreate.None;   // repairing live data, never migrating it
});

await using var session = store.LightweightSession();
session.SetHeader(EventActor.HeaderKey, RestoreActor);

var events = await session.Events.QueryAllRawEvents()
    .Where(e => eventTypes.Contains(e.EventTypeName))
    .OrderBy(e => e.Sequence)
    .ToListAsync();

var touched = 0;
foreach (var stream in events.GroupBy(e => e.StreamId).OrderBy(g => g.Key))
{
    var name = "";
    List<ContactRelation> relations = [];
    List<Guid> emergency = [];
    // First wipe wins: a later one's "before" state is already empty, so only the earliest holds the lost edges.
    List<ContactRelation>? lostRelations = null;
    List<Guid>? lostEmergency = null;

    foreach (var e in stream.OrderBy(e => e.Version))
        switch (e.Data)
        {
            case ContactCreated c: name = Name(c.Fields); break;
            case ContactImported c: name = Name(c.Parsed); break;
            case ContactRevised c: name = Name(c.Fields); break;

            case ContactRelationAdded d:
                relations.RemoveAll(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind);
                relations.Add(new ContactRelation { ToContactId = d.ToContactId, Kind = d.Kind, Label = d.Label, Since = d.Since, Note = d.Note });
                break;
            case ContactRelationRemoved d:
                relations.RemoveAll(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind);
                break;
            case ContactRelationEnded d:
                if (relations.FirstOrDefault(r => r.ToContactId == d.ToContactId && r.Kind == d.Kind) is { } edge)
                    (edge.Ended, edge.Until) = (true, d.Until);
                break;
            case ContactRelationsReplaced d:
                if (d.Relations.Count == 0 && relations.Count > 0) lostRelations ??= relations;
                relations = [.. d.Relations];
                break;
            case ContactEmergencyContactsReplaced d:
                if (d.ContactIds.Count == 0 && emergency.Count > 0) lostEmergency ??= emergency;
                emergency = [.. d.ContactIds];
                break;
        }

    // Union, not overwrite: edges added after the wipe are real and must survive the repair.
    var restoredRelations = relations
        .Concat((lostRelations ?? []).Where(r => !relations.Any(x => x.ToContactId == r.ToContactId && x.Kind == r.Kind)))
        .ToList();
    var restoredEmergency = emergency.Concat((lostEmergency ?? []).Where(x => !emergency.Contains(x))).ToList();

    var addedRelations = restoredRelations.Count - relations.Count;
    var addedEmergency = restoredEmergency.Count - emergency.Count;
    if (addedRelations == 0 && addedEmergency == 0) continue;

    Console.WriteLine($"{stream.Key}  {name,-32} +{addedRelations} relation(s)  +{addedEmergency} emergency");
    foreach (var r in restoredRelations.Skip(relations.Count))
        Console.WriteLine($"    {r.Kind,-12} -> {r.ToContactId}{(r.Ended ? "  (ended)" : "")}{(r.Label is null ? "" : $"  \"{r.Label}\"")}");
    touched++;

    if (!apply) continue;
    if (addedRelations > 0) session.Events.Append(stream.Key, new ContactRelationsReplaced(stream.Key, restoredRelations));
    if (addedEmergency > 0) session.Events.Append(stream.Key, new ContactEmergencyContactsReplaced(stream.Key, restoredEmergency));
}

if (apply) await session.SaveChangesAsync();
Console.WriteLine($"\n{touched} contact(s) {(apply ? "restored" : "would be restored — re-run with --apply")}.");

static string Name(ContactFields f) => string.Join(' ', new[] { f.GivenName, f.FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s)));
