using JasperFx.Events;
using Lupira.Identity.Marten;
using Lupira.Primitives;
using LupiraContactApi.Core.Domain.PlaceEntries.Events;

namespace LupiraContactApi.Core.Domain.PlaceEntries;

/// <summary>
/// How to get in at a place + inline snapshot: its door and gate codes. One per geo place — a code belongs to the
/// building, shared by everyone living there, not to a person. The event stream is the audit trail of who set what.
/// Visibility is derived (see <c>PlaceEntryService</c>): only readers of a current resident see it.
/// </summary>
public sealed class PlaceEntry
{
    public Guid Id { get; set; }

    public Guid PlaceId { get; set; }

    public List<EntryCode> Codes { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public static Guid StreamIdOf(Guid placeId) => DeterministicGuid.From($"place-entry:{placeId:D}");

    public static IReadOnlyList<object> Set(PlaceEntry? current, Guid placeId, Guid codeId, string label, string code, string? note) =>
        current?.Codes.FirstOrDefault(c => c.Id == codeId) is { } existing && (existing.Label, existing.Code, existing.Note) == (label, code, note)
            ? []
            : [new EntryCodeSet(placeId, codeId, label, code, note)];

    public IReadOnlyList<object> Remove(Guid codeId) => Codes.Any(c => c.Id == codeId) ? [new EntryCodeRemoved(PlaceId, codeId)] : [];

    public void Apply(IEvent<EntryCodeSet> e)
    {
        var d = e.Data;
        (Id, PlaceId) = (e.StreamId, d.PlaceId);
        Codes.RemoveAll(c => c.Id == d.CodeId);
        Codes.Add(new EntryCode { Id = d.CodeId, Label = d.Label, Code = d.Code, Note = d.Note });
        Touch(e);
    }

    public void Apply(IEvent<EntryCodeRemoved> e)
    {
        Codes.RemoveAll(c => c.Id == e.Data.CodeId);
        Touch(e);
    }

    private void Touch(IEvent e)
    {
        UpdatedAt = e.Timestamp;
        UpdatedBy = EventActor.Of(e);
    }
}
