using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts;

/// <summary>An address as legacy contact events carried it: a geo place id, a type, and fuzzy residency boundaries.
/// Legacy: residencies are their own aggregate now (<see cref="Residencies.Residency"/>); kept so old contact streams still
/// deserialize, and read only by the residencies migration.</summary>
public sealed class ContactPostalAddress
{
    public required Guid PlaceId { get; set; }

    public ContactAddressType Type { get; set; }

    public FuzzyDate? MovedIn { get; set; }

    public FuzzyDate? MovedOut { get; set; }
}
