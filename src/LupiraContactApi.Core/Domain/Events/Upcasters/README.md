# Event upcasters

Events are immutable once written. To change an event's **shape** after go-live, do NOT edit the record in
place — add an upcaster here and register it in `MartenRegistrations.UseLupiraContact`:

```csharp
opts.Events.Upcast<ContactRevisedV1ToV2>();
```

Renaming or moving an event **type** is safe without an upcaster because every event has an explicit stable name
via `MapEventType` (see `MartenRegistrations`). Adding a new event type needs only a new `MapEventType` line.

The current schema was reset at greenfield, so there are no upcasters yet — this folder documents the convention.
