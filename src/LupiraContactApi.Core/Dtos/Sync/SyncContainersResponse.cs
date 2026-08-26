using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;

namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>Snapshot of the caller's containers. Address books are plain documents (no cursor); groups are
/// event-sourced but small and fully replaced each cycle — both are fetched once per sync cycle and diffed
/// against the mirror.</summary>
public sealed class SyncContainersResponse
{
    public required IReadOnlyList<AddressBookDto> AddressBooks { get; set; }
    public required IReadOnlyList<ContactGroupDto> Groups { get; set; }
}
