using JasperFx.Events;
using Lupira.Identity.Marten;
using LupiraContactApi.Core.Domain.Residencies.Events;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Residencies;

/// <summary>
/// A contact's tie to a geo place over a period + inline snapshot: where they live, holiday, work. Its own record, so
/// edits to one residency never clash with another, a move is told as a move, and "who lives here" is an indexed query.
/// <see cref="FuzzyDate"/> boundaries are as precise as actually known, null = unknown; currency is
/// <see cref="IsActiveOn"/>, never "MovedOut set". Moving back to a place is a second residency. No FK to the contact.
/// </summary>
public sealed class Residency
{
    public Guid Id { get; set; }

    public Guid ContactId { get; set; }

    public Guid PlaceId { get; set; }

    public ContactAddressType Type { get; set; }

    /// <summary>Free-text refinement ("Summer house, Gotland").</summary>
    public string? Label { get; set; }

    public FuzzyDate? MovedIn { get; set; }

    public FuzzyDate? MovedOut { get; set; }

    /// <summary>Erased as a mistake; kept as a tombstone so sync feeds report the removal.</summary>
    public bool Removed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    /// <summary>Global event sequence of the last event applied — the residencies sync feed's watermark (indexed).</summary>
    public long UpdatedSequence { get; set; }

    public bool IsLive => Id != Guid.Empty && !Removed;

    public static IReadOnlyList<object> Start(
        Guid id, Guid contactId, Guid placeId, ContactAddressType type, string? label, FuzzyDate? movedIn, FuzzyDate? movedOut) =>
        [new ResidencyStarted(id, contactId, placeId, type, label, movedIn, movedOut)];

    /// <summary>Today falls inside the period; ambiguity resolves toward active.</summary>
    public bool IsActiveOn(DateOnly today) =>
        (MovedIn is null || !MovedIn.IsCertainlyFuture(today)) &&
        (MovedOut is null || !MovedOut.IsCertainlyPast(today));

    public IReadOnlyList<object> Revise(Guid placeId, ContactAddressType type, string? label, FuzzyDate? movedIn, FuzzyDate? movedOut) =>
        (PlaceId, Type, Label, MovedIn, MovedOut) == (placeId, type, label, movedIn, movedOut)
            ? []
            : [new ResidencyRevised(Id, placeId, type, label, movedIn, movedOut)];

    public IReadOnlyList<object> MoveOut(FuzzyDate movedOut) => MovedOut == movedOut ? [] : [new ResidencyMovedOut(Id, movedOut)];

    public IReadOnlyList<object> Remove() => [new ResidencyRemoved(Id)];

    public void Apply(IEvent<ResidencyStarted> e)
    {
        var d = e.Data;
        (Id, ContactId, PlaceId, Type, Label, MovedIn, MovedOut) = (d.ResidencyId, d.ContactId, d.PlaceId, d.Type, d.Label, d.MovedIn, d.MovedOut);
        CreatedAt = e.Timestamp;
        CreatedBy = EventActor.Of(e);
        Touch(e);
    }

    public void Apply(IEvent<ResidencyRevised> e)
    {
        var d = e.Data;
        (PlaceId, Type, Label, MovedIn, MovedOut) = (d.PlaceId, d.Type, d.Label, d.MovedIn, d.MovedOut);
        Touch(e);
    }

    public void Apply(IEvent<ResidencyMovedOut> e)
    {
        MovedOut = e.Data.MovedOut;
        Touch(e);
    }

    public void Apply(IEvent<ResidencyRemoved> e)
    {
        Removed = true;
        Touch(e);
    }

    private void Touch(IEvent e)
    {
        UpdatedAt = e.Timestamp;
        UpdatedBy = EventActor.Of(e);
        UpdatedSequence = e.Sequence;
    }
}
