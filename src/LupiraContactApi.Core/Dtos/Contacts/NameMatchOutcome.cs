namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Per-name match outcome. <c>Matched</c> = exactly one contact whose normalized display name equals the
/// query (or the lone substring hit); <c>Ambiguous</c> = several candidates; <c>NotFound</c> = no substring hit.</summary>
public enum NameMatchOutcome
{
    Matched,
    Ambiguous,
    NotFound,
}
