namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Per-name match outcome, compared case-, diacritic- and punctuation-insensitively. <c>Matched</c> = exactly one
/// contact whose full name form (given+family, given+middle+family, nickname, nickname+family or display name) equals
/// the query, or else the lone contact holding every token of a multi-token query; <c>Ambiguous</c> = several such
/// contacts, or a lone token hit on a one-token query; <c>NotFound</c> = no contact holds every query token.</summary>
public enum NameMatchOutcome
{
    Matched,
    Ambiguous,
    NotFound,
}
