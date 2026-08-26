using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Inference;

/// <summary>Derives social circles around a focus contact from relation edges, the kinship graph, shared organization
/// membership, and shared home places. Pure over supplied data like <see cref="KinshipInference"/>; computed on read,
/// never stored. Ended edges assert no current relationship and are ignored.</summary>
public static class CircleInference
{
    public static IReadOnlyList<CircleMembership> Infer(
        Guid focusId, IReadOnlyCollection<Contact> contacts, IReadOnlyCollection<ContactGroup> organizations,
        DateOnly? today = null)
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

        // Explicit live edges, both directions, resolved to the other contact's role relative to the focus.
        // Extended kinds land here too when stored explicitly (linking relative not a contact); CircleOf keeps
        // them consistent with the inferred ones, and per-circle dedup lets an explicit membership win.
        foreach (var c in contacts)
        {
            foreach (var r in c.Relations.Where(r => !r.Ended))
            {
                Guid other;
                ContactRelationKind kind;
                if (c.Id == focusId)
                {
                    other = r.ToContactId;
                    kind = r.Kind;
                }
                else if (r.ToContactId == focusId)
                {
                    other = c.Id;
                    kind = r.Kind.Inverse();
                }
                else
                {
                    continue;
                }

                if (CircleOf(kind) is ({ } ck, var degree)) Add(ck, other, kind, degree, RelationProvenance.Explicit);
            }
        }

        // Kinship graph: inferred siblings are close family; two-generation kin and cousins are extended.
        foreach (var kin in KinshipInference.Infer(focusId, contacts))
            if (CircleOf(kin.Kind) is ({ } ck, var degree)) Add(ck, kin.ContactId, kin.Kind, degree, RelationProvenance.Inferred);

        // Shared employer: co-members of a live Organization-kind group.
        foreach (var org in organizations.Where(g => g.Kind == ContactGroupKind.Organization && g.DeletedAt is null && g.Members.Any(m => m.ContactId == focusId)))
        {
            foreach (var member in org.Members)
                Add(CircleKind.Colleagues, member.ContactId, ContactRelationKind.Colleague, 1, RelationProvenance.Inferred);
        }

        // Household: a shared geo place on an ACTIVE Home address — past/future residencies assert no current
        // cohabitation, same rule as ended relation edges above.
        var focus = contacts.First(c => c.Id == focusId);
        var homePlaces = focus.Addresses.Where(a => a.Type == ContactAddressType.Home && a.IsActiveOn(day))
            .Select(a => a.PlaceId).ToHashSet();
        if (homePlaces.Count > 0)
        {
            foreach (var c in contacts)
            {
                if (c.Addresses.Any(a => a.Type == ContactAddressType.Home && a.IsActiveOn(day) && homePlaces.Contains(a.PlaceId)))
                    Add(CircleKind.Household, c.Id, null, 1, RelationProvenance.Inferred);
            }
        }

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
