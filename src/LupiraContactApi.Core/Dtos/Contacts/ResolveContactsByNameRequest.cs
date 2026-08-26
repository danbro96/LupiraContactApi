namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Batch-match a list of free-text names to existing contacts (import disambiguation). Optionally scope to
/// one address book; otherwise all the caller's accessible books.</summary>
public sealed class ResolveContactsByNameRequest
{
    public required List<string> Names { get; set; }
    public Guid? AddressBookId { get; set; }
}

/// <summary>Per-name match outcome. <c>Matched</c> = exactly one contact whose normalized display name equals the
/// query (or the lone substring hit); <c>Ambiguous</c> = several candidates; <c>NotFound</c> = no substring hit.</summary>
public enum NameMatchOutcome { Matched, Ambiguous, NotFound }

/// <summary>A lightweight contact reference (id + display name) — a resolve candidate.</summary>
public sealed class ContactRef
{
    public required Guid ContactId { get; set; }
    public required string DisplayName { get; set; }
}

/// <summary>Resolution of one input name. On <c>Matched</c>, <c>ContactId</c> is set; <c>Candidates</c> always lists
/// the considered contacts (capped) so the caller can disambiguate.</summary>
public sealed class ContactNameMatch
{
    public required string Name { get; set; }
    public Guid? ContactId { get; set; }
    public required NameMatchOutcome Outcome { get; set; }
    public required List<ContactRef> Candidates { get; set; }
}
