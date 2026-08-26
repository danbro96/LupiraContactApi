using LupiraContactApi.Core.Domain.Completeness;
using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Resolves the derived completeness score for contacts. It lives outside the snapshot because a contact's
/// organisation/role lives on a separate <see cref="ContactGroup"/>, and relation edges are stored one-directional —
/// a contact can be connected purely by inbound edges on other aggregates.</summary>
public sealed class CompletenessResolver(IQuerySession session)
{
    public async Task<CompletenessScore?> ScoreContactAsync(Contact c, CancellationToken ct = default) =>
        (await ScoreContactsAsync([c], ct))[c.Id];

    public async Task<Dictionary<Guid, CompletenessScore?>> ScoreContactsAsync(IReadOnlyCollection<Contact> contacts, CancellationToken ct = default)
    {
        var orgMembers = await OrganisationMemberIdsAsync([.. contacts.Select(c => c.Id)], ct);
        var related = await InboundRelationTargetIdsAsync(ct);
        return contacts.ToDictionary(c => c.Id, c => CompletenessScorer.ScoreContact(c, orgMembers.Contains(c.Id), related.Contains(c.Id)));
    }

    private async Task<HashSet<Guid>> OrganisationMemberIdsAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        if (contactIds.Count == 0) return [];
        var idSet = contactIds.ToHashSet();
        var groups = await session.Query<ContactGroup>().Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt == null).ToListAsync(ct);
        return [.. groups.SelectMany(g => g.Members.Select(m => m.ContactId)).Where(idSet.Contains)];
    }

    // Every ToContactId referenced by a live contact's edges — the reverse direction of the one-directional storage.
    private async Task<HashSet<Guid>> InboundRelationTargetIdsAsync(CancellationToken ct)
    {
        var live = await session.Query<Contact>().Where(c => c.DeletedAt == null).ToListAsync(ct);
        return [.. live.SelectMany(c => c.Relations.Select(r => r.ToContactId))];
    }
}
