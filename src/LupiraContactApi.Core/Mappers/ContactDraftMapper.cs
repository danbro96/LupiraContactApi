using System.Security.Cryptography;
using System.Text;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Serialization;

namespace LupiraContactApi.Core.Mappers;

internal static class ContactDraftMapper
{
    public static ContactDraftDto ToDraft(this ParsedContact p, Guid principalId)
    {
        var given = Clean(p.GivenName);
        var middle = Clean(p.MiddleName);
        var family = Clean(p.FamilyName);
        var organization = Clean(p.Organization);
        var unnamed = given is null && middle is null && family is null;
        var kind = p.Kind ?? (unnamed && organization is not null ? ContactKind.Organization : ContactKind.Individual);
        if (unnamed) given = (kind == ContactKind.Organization ? organization : null) ?? Clean(p.FullName);

        var preferredSeen = new HashSet<ReachMedium>();
        var draft = new ContactDraftDto
        {
            SourceKey = string.Empty,
            Kind = kind,
            GivenName = given,
            MiddleName = middle,
            FamilyName = family,
            Nickname = Clean(p.Nickname),
            Organization = kind == ContactKind.Individual ? organization : null,
            Channels = [.. ReachChannelNormalizer.Normalize(p.Channels ?? [])
                .Select(c => c.Preferred && !preferredSeen.Add(c.Medium) ? c with { Preferred = false } : c)],
            Birthday = p.Birthday,
            Notes = Clean(p.Notes),
            Pronouns = Clean(p.Pronouns),
        };
        draft.SourceKey = SourceKey(principalId, p.Uid is { } uid ? $"uid\n{uid}" : $"content\n{Fingerprint(draft)}");
        return draft;
    }

    // Contact streams are keyed by the source key alone, so a file two members both import must not yield one shared key.
    private static string SourceKey(Guid principalId, string identity) =>
        "import-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{principalId:N}\n{identity}")))[..32];

    private static string Fingerprint(ContactDraftDto d) => string.Join('\n',
        [d.Kind.ToString(), d.GivenName, d.MiddleName, d.FamilyName, d.Nickname, d.Organization,
            .. d.Channels.Select(c => $"{c.Medium}|{c.Value}|{c.Type}|{c.Preferred}"),
            d.Birthday?.ToCanonical(), d.Notes, d.Pronouns]);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
