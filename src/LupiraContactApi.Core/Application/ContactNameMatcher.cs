using System.Globalization;
using System.Text;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Dtos.Contacts;

namespace LupiraContactApi.Core.Application;

/// <summary>Name matching over folded text (case, diacritics, punctuation). A full form is an exact folded match on
/// given+family, given+middle+family, nickname, nickname+family or the display name; a token match is every query
/// token being one of the contact's name tokens.</summary>
public static class ContactNameMatcher
{
    public const int MaxCandidates = 5;

    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSeparator && sb.Length > 0) sb.Append(' ');
                pendingSeparator = false;
                var lower = char.ToLowerInvariant(ch);

                // FormD has no decomposition for these, so they would never meet their ASCII spelling.
                switch (lower)
                {
                    case 'ø': sb.Append('o'); break;
                    case 'æ': sb.Append("ae"); break;
                    case 'ß': sb.Append("ss"); break;
                    default: sb.Append(lower); break;
                }
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return sb.ToString();
    }

    public static string[] Tokens(string text) => Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static HashSet<string> FullForms(Contact contact)
    {
        string?[][] forms =
        [
            [contact.GivenName, contact.FamilyName],
            [contact.GivenName, contact.MiddleName, contact.FamilyName],
            [contact.Nickname],
            [contact.Nickname, contact.FamilyName],
            [contact.DisplayName],
        ];
        return [.. forms.Where(parts => parts.All(p => !string.IsNullOrWhiteSpace(p))).Select(parts => Fold(string.Join(' ', parts))).Where(f => f.Length > 0)];
    }

    public static HashSet<string> NameTokens(Contact contact) =>
        [.. new[] { contact.GivenName, contact.MiddleName, contact.FamilyName, contact.Nickname }.SelectMany(p => Tokens(p ?? string.Empty))];

    public static bool IsTokenMatch(IReadOnlySet<string> nameTokens, IReadOnlyCollection<string> queryTokens) =>
        queryTokens.Count > 0 && queryTokens.All(nameTokens.Contains);

    /// <summary>Contacts with a token match, or whose folded name text contains the folded query (so prefixes match).</summary>
    public static IEnumerable<Contact> Search(IEnumerable<Contact> contacts, string query)
    {
        var folded = Fold(query);
        if (folded.Length == 0) return [];
        var queryTokens = folded.Split(' ');
        return contacts.Where(c => IsTokenMatch(NameTokens(c), queryTokens) || Fold(c.SearchText).Contains(folded, StringComparison.Ordinal));
    }

    /// <summary>One full-form hit → Matched; several → Ambiguous over those. Otherwise a lone token hit on a query of
    /// two or more tokens → Matched; any other token hits → Ambiguous; none → NotFound.</summary>
    public static List<ContactNameMatch> Resolve(IReadOnlyList<string?> names, IReadOnlyCollection<Contact> pool)
    {
        var indexed = pool.Select(c => (Contact: c, Forms: FullForms(c), Tokens: NameTokens(c))).ToList();
        var results = new List<ContactNameMatch>(names.Count);
        foreach (var raw in names)
        {
            var name = raw ?? string.Empty;
            var folded = Fold(name);
            var queryTokens = folded.Length == 0 ? [] : folded.Split(' ');

            var fullHits = indexed.Where(e => e.Forms.Contains(folded)).Select(e => e.Contact).ToList();
            var tokenHits = fullHits.Count > 0 ? [] : indexed.Where(e => IsTokenMatch(e.Tokens, queryTokens)).Select(e => e.Contact).ToList();

            var (outcome, refs) = (fullHits.Count, tokenHits.Count) switch
            {
                (1, _) => (NameMatchOutcome.Matched, fullHits),
                (> 1, _) => (NameMatchOutcome.Ambiguous, fullHits),
                (_, 1) when queryTokens.Length >= 2 => (NameMatchOutcome.Matched, tokenHits),
                (_, > 0) => (NameMatchOutcome.Ambiguous, tokenHits),
                _ => (NameMatchOutcome.NotFound, new List<Contact>()),
            };

            results.Add(new ContactNameMatch
            {
                Name = name,
                ContactId = outcome == NameMatchOutcome.Matched ? refs[0].Id : null,
                Outcome = outcome,
                Candidates = [.. refs.OrderBy(c => c.SortName).Take(MaxCandidates).Select(c => new ContactRef { ContactId = c.Id, DisplayName = c.DisplayName })],
            });
        }

        return results;
    }
}
