using System.Globalization;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Serialization;
using static Lupira.Primitives.ContentHash;

namespace LupiraContactApi.Core.Application;

/// <summary>A contact as the sync adapter serves it: its own content plus its relationships as seen from it. The ETag
/// covers both, so a relationship edit moves the ETag of both cards it appears on.</summary>
public sealed record DavCard(Contact Contact, IReadOnlyList<ResolvedRelation> Relations)
{
    public string Etag => Relations.Count == 0 && !CarriesLateNameParts
        ? Contact.ContentHash
        : Of(string.Join('\n', [Contact.ContentHash, .. CarriesLateNameParts ? ["names"] : Array.Empty<string>(), .. Relations.Select(Line)]));

    public string Vcard => VCardSerializer.From(Contact, Relations);

    // Middle name and nickname reached the card after phones cached it; moving those ETags makes a stale copy fail its precondition instead of clearing them.
    private bool CarriesLateNameParts => Contact.MiddleName is not null || Contact.Nickname is not null;

    // The fields the card carries (the note is not on this surface, so it doesn't move the ETag).
    private static string Line(ResolvedRelation r) =>
        string.Join('|', r.OtherId.ToString("D"), r.Kind, r.Label, Date(r.Since), r.Ended ? "1" : "0", Date(r.Until));

    private static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}
