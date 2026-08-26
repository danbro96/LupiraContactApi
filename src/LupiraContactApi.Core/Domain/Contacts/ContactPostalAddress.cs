using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts;

/// <summary>A contact's postal address: a LupiraGeoApi place id (the sole source of truth — no free-text) with a home/work
/// type. <see cref="FuzzyDate"/> boundaries are as precise as actually known, null = unknown; currency is
/// <see cref="IsActiveOn"/>, never "MovedOut set".</summary>
public sealed class ContactPostalAddress
{
    public required Guid PlaceId { get; set; }

    public ContactAddressType Type { get; set; }

    public FuzzyDate? MovedIn { get; set; }

    public FuzzyDate? MovedOut { get; set; }

    /// <summary>Today falls inside the period; ambiguity resolves toward active.</summary>
    public bool IsActiveOn(DateOnly today) =>
        (MovedIn is null || !MovedIn.IsCertainlyFuture(today)) &&
        (MovedOut is null || !MovedOut.IsCertainlyPast(today));
}
