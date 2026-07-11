using LupiraContactApi.Domain;
using Marten;

namespace LupiraContactApi.Application;

/// <summary>Resolves the derived completeness score for contacts. It lives outside the snapshot because a contact's
/// organisation/role lives on a separate <see cref="ContactGroup"/> — not visible to a single-stream snapshot.</summary>
public sealed class CompletenessResolver(IQuerySession session)
{
    public async Task<CompletenessScore?> ScoreContactAsync(Contact c, CancellationToken ct = default)
    {
        var orgMembers = await OrganisationMemberIdsAsync([c.Id], ct);
        return CompletenessScorer.ScoreContact(c, orgMembers.Contains(c.Id));
    }

    public async Task<Dictionary<Guid, CompletenessScore?>> ScoreContactsAsync(IReadOnlyCollection<Contact> contacts, CancellationToken ct = default)
    {
        var orgMembers = await OrganisationMemberIdsAsync([.. contacts.Select(c => c.Id)], ct);
        return contacts.ToDictionary(c => c.Id, c => CompletenessScorer.ScoreContact(c, orgMembers.Contains(c.Id)));
    }

    private async Task<HashSet<Guid>> OrganisationMemberIdsAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        if (contactIds.Count == 0) return [];
        var idSet = contactIds.ToHashSet();
        var groups = await session.Query<ContactGroup>().Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt == null).ToListAsync(ct);
        return [.. groups.SelectMany(g => g.Members.Select(m => m.ContactId)).Where(idSet.Contains)];
    }
}
