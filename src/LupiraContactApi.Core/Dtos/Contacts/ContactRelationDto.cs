using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>A relation copy this contact's own record holds: "the <c>ToContactId</c> contact is my <c>Kind</c>". Storage, not the
/// relationship — the other side may hold a copy too, so render <c>GET /contacts/{id}/relations</c>, which merges both.
/// <c>Ended</c>/<c>Until</c> mark a relationship that ran its course.</summary>
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
