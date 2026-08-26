using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>One outgoing relation edge as published: "the <c>ToContactId</c> contact is my <c>Kind</c>".
/// <c>Ended</c>/<c>Until</c> mark a relationship that ran its course, distinct from removal.</summary>
public sealed class ContactRelationDto
{
    public required Guid ToContactId { get; set; }
    public required ContactRelationKind Kind { get; set; }
    public required string? Label { get; set; }
    public required DateOnly? Since { get; set; }
    public required string? Note { get; set; }
    public required bool Ended { get; set; }
    public required DateOnly? Until { get; set; }
}
