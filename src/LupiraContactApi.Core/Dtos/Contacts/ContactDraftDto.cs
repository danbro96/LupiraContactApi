using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>A contact read from a contacts file, not yet saved. Fields match <see cref="CreateContactRequest"/> one to one,
/// except <c>organization</c>, which is set as membership in an organization group.</summary>
public sealed class ContactDraftDto
{
    /// <summary>Pass as the create request's <c>sourceKey</c>: importing the same contact again then returns the existing
    /// contact instead of duplicating it. Stable per caller and contact; differs between callers.</summary>
    public required string SourceKey { get; set; }

    /// <summary>Organization when the file marks the contact as one, or when it carries only an organization name.</summary>
    public required ContactKind Kind { get; set; }

    /// <summary>For an Organization, its name.</summary>
    public string? GivenName { get; set; }

    public string? MiddleName { get; set; }

    public string? FamilyName { get; set; }

    public string? Nickname { get; set; }

    /// <summary>The employer of an Individual, when the file names one.</summary>
    public string? Organization { get; set; }

    /// <summary>Trimmed and de-duplicated, at most one preferred per medium.</summary>
    public required List<ContactReachChannel> Channels { get; set; }

    public PartialDate? Birthday { get; set; }

    public string? Notes { get; set; }

    public string? Pronouns { get; set; }
}
