using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Inference;

/// <summary>Derives social circles around a focus contact from relationships, the kinship graph, shared organization
/// membership, and shared home places. Pure over supplied data like <see cref="KinshipInference"/>; computed on read,
/// never stored. Ended relationships assert no current tie and are ignored.</summary>
public static class CircleInference
{
    public static IReadOnlyList<CircleMembership> Infer(
        Guid focusId, IReadOnlyCollection<Contact> contacts, IReadOnlyCollection<Relationship> relationships,
        IReadOnlyCollection<Residency> residencies, IReadOnlyCollection<ContactGroup> organizations, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var known = contacts.Select(c => c.Id).ToHashSet();
        if (!known.Contains(focusId)) return [];

        var result = new List<CircleMembership>();
        var perCircle = new Dictionary<CircleKind, HashSet<Guid>>();
        void Add(CircleKind circle, Guid id, ContactRelationKind? kind, int degree, RelationProvenance provenance)
        {
            if (id == focusId || !known.Contains(id)) return;
            if (!perCircle.TryGetValue(circle, out var seen)) perCircle[circle] = seen = [];
            if (seen.Add(id)) result.Add(new CircleMembership(circle, id, kind, degree, provenance));
        }

        // Explicit live relationships, seen from the focus. Extended kinds land here too when stored explicitly (linking
        // relative not a contact); CircleOf keeps them consistent with the inferred ones, and per-circle dedup lets an
        // explicit membership win.
        foreach (var r in relationships.Where(r => r.IsLive && !r.Ended && r.Involves(focusId)))
        {
            var kind = r.Key.KindSeenFrom(focusId);
            if (CircleOf(kind) is ({ } ck, var degree)) Add(ck, r.Key.OtherThan(focusId), kind, degree, RelationProvenance.Explicit);
        }

        // Kinship graph: inferred siblings are close family; two-generation kin and cousins are extended.
        foreach (var kin in KinshipInference.Infer(focusId, known, relationships))
            if (CircleOf(kin.Kind) is ({ } ck, var degree)) Add(ck, kin.ContactId, kin.Kind, degree, RelationProvenance.Inferred);

        // Shared employer: co-members of a live Organization-kind group.
        foreach (var org in organizations.Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt is null && g.Members.Any(m => m.ContactId == focusId)))
        {
            foreach (var member in org.Members)
                Add(CircleKind.Colleagues, member.ContactId, ContactRelationKind.Colleague, 1, RelationProvenance.Inferred);
        }

        // Household: a shared geo place on a CURRENT Home residency — past/future residencies assert no current cohabitation,
        // same rule as ended relationships above, and a shared vacation home makes no household.
        var homes = residencies.Where(r => r.IsLive && r.Type == ContactAddressType.Home && r.IsActiveOn(day)).ToList();
        var focusHomes = homes.Where(r => r.ContactId == focusId).Select(r => r.PlaceId).ToHashSet();
        foreach (var r in homes.Where(r => focusHomes.Contains(r.PlaceId)))
            Add(CircleKind.Household, r.ContactId, null, 1, RelationProvenance.Inferred);

        return result;
    }

    // The circle + closeness degree a relation kind maps to (relative to the focus); null = no circle.
    // Shared by the explicit-edge and inferred passes so both representations of a kind land identically.
    private static (CircleKind? Circle, int Degree) CircleOf(ContactRelationKind kind) => kind switch
    {
        ContactRelationKind.Spouse or ContactRelationKind.Partner or ContactRelationKind.Parent
            or ContactRelationKind.Child or ContactRelationKind.Sibling => (CircleKind.CloseFamily, 1),
        ContactRelationKind.Grandparent or ContactRelationKind.Grandchild
            or ContactRelationKind.AuntUncle or ContactRelationKind.NieceNephew => (CircleKind.ExtendedFamily, 2),
        ContactRelationKind.Cousin => (CircleKind.ExtendedFamily, 3),
        ContactRelationKind.Friend => (CircleKind.Friends, 1),
        ContactRelationKind.Colleague => (CircleKind.Colleagues, 1),
        _ => (null, 0),
    };
}
