namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Resolution of one input name. On <c>Matched</c>, <c>ContactId</c> is set; <c>Candidates</c> always lists
/// the considered contacts (capped) so the caller can disambiguate.</summary>
public sealed class ContactNameMatch
{
    public required string Name { get; set; }
    public Guid? ContactId { get; set; }
    public required NameMatchOutcome Outcome { get; set; }
    public required List<ContactRef> Candidates { get; set; }
}
