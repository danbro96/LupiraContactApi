namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Batch-match a list of free-text names to existing contacts (import disambiguation). Optionally scope to
/// one address book; otherwise all the caller's accessible books.</summary>
public sealed class ResolveContactsByNameRequest
{
    public required List<string> Names { get; set; }

    public Guid? AddressBookId { get; set; }
}
