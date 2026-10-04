using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Completeness;
using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Resolves the derived completeness score for contacts. It lives outside the snapshot because a contact's
/// organisation/role lives on a separate <see cref="ContactGroup"/>, and its relationships and residencies on their own aggregates.</summary>
public sealed class CompletenessResolver(IQuerySession session)
{
    public async Task<CompletenessScore?> ScoreContactAsync(Contact c, CancellationToken ct = default) =>
        (await ScoreContactsAsync([c], ct))[c.Id];

    public async Task<Dictionary<Guid, CompletenessScore?>> ScoreContactsAsync(IReadOnlyCollection<Contact> contacts, CancellationToken ct = default)
    {
        var ids = contacts.Select(c => c.Id).ToList();
        var orgMembers = await OrganisationMemberIdsAsync(ids, ct);
        var related = (await session.RelationshipsOfAsync(ids, ct)).SelectMany(r => new[] { r.Low, r.High }).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var addressed = (await session.ResidenciesOfAsync(ids, ct)).Where(r => r.IsActiveOn(today)).Select(r => r.ContactId).ToHashSet();
        return contacts.ToDictionary(c => c.Id, c => CompletenessScorer.ScoreContact(c, orgMembers.Contains(c.Id), related.Contains(c.Id), addressed.Contains(c.Id)));
    }

    private async Task<HashSet<Guid>> OrganisationMemberIdsAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        if (contactIds.Count == 0) return [];
        var idSet = contactIds.ToHashSet();
        var groups = await session.Query<ContactGroup>().Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt == null).ToListAsync(ct);
        return [.. groups.SelectMany(g => g.Members.Select(m => m.ContactId)).Where(idSet.Contains)];
    }
}
