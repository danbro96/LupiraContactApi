using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Inference;

/// <summary>
/// Derives family relationships from stored <c>Parent</c>/<c>Child</c>/<c>Sibling</c> relationships. Bounded to a two-generation closure: siblings, grandparents/grandchildren, aunts-uncles, nieces-nephews, cousins.
/// </summary>
public static class KinshipInference
{
    /// <summary>Inferred kin of <paramref name="focusId"/>, one role per contact (closest wins), excluding the focus and
    /// anyone it already relates to explicitly (explicit relationships win — the caller surfaces those separately). Only
    /// <paramref name="known"/> contacts are returned.</summary>
    public static IReadOnlyList<InferredKin> Infer(Guid focusId, IReadOnlySet<Guid> known, IEnumerable<Relationship> relationships)
    {
        if (!known.Contains(focusId)) return [];
        var g = new Graph(relationships);

        var explicitPartners = g.ExplicitPartners(focusId);
        var parents = g.Parents(focusId);
        var children = g.Children(focusId);

        // Kinds in precedence order; first assignment for a contact wins.
        var buckets = new (ContactRelationKind Kind, IEnumerable<Guid> Ids)[]
        {
            (ContactRelationKind.Sibling, g.Siblings(focusId)),
            (ContactRelationKind.Grandparent, parents.SelectMany(g.Parents)),
            (ContactRelationKind.Grandchild, children.SelectMany(g.Children)),
            (ContactRelationKind.AuntUncle, parents.SelectMany(g.Siblings).Except(parents)),
            (ContactRelationKind.NieceNephew, g.Siblings(focusId).SelectMany(g.Children)),
            (ContactRelationKind.Cousin, parents.SelectMany(g.Siblings).Except(parents).SelectMany(g.Children)),
        };

        var seen = new HashSet<Guid> { focusId };
        seen.UnionWith(explicitPartners);
        var result = new List<InferredKin>();
        foreach (var (kind, ids) in buckets)
            foreach (var id in ids)
            {
                if (known.Contains(id) && seen.Add(id))
                    result.Add(new InferredKin(id, kind));
            }

        return result;
    }

    /// <summary>True when recording <paramref name="parentId"/> as a parent of <paramref name="childId"/> would make
    /// someone their own ancestor. BFS over transitive parents; the visited set keeps pre-existing bad data from looping.</summary>
    public static bool WouldCreateParentCycle(Guid childId, Guid parentId, IEnumerable<Relationship> relationships)
    {
        if (childId == parentId) return true;
        var g = new Graph(relationships);
        var queue = new Queue<Guid>([parentId]);
        var visited = new HashSet<Guid> { parentId };
        while (queue.TryDequeue(out var current))
        {
            foreach (var p in g.Parents(current))
            {
                if (p == childId) return true;
                if (visited.Add(p)) queue.Enqueue(p);
            }
        }

        return false;
    }

    // Adjacency over live parent/child/sibling relationships ("High is Low's Kind"); ended ones assert no current kinship.
    private sealed class Graph
    {
        private readonly Dictionary<Guid, HashSet<Guid>> _parents = new();   // x -> parents of x
        private readonly Dictionary<Guid, HashSet<Guid>> _children = new();  // x -> children of x
        private readonly Dictionary<Guid, HashSet<Guid>> _siblings = new();  // x -> explicit siblings of x
        private readonly Dictionary<Guid, HashSet<Guid>> _partners = new();  // x -> explicit edge partners (any kind)

        public Graph(IEnumerable<Relationship> relationships)
        {
            foreach (var r in relationships.Where(r => r.IsLive && !r.Ended))
            {
                Link(_partners, r.Low, r.High);
                Link(_partners, r.High, r.Low);
                switch (r.Kind)
                {
                    case ContactRelationKind.Parent: Link(_parents, r.Low, r.High); Link(_children, r.High, r.Low); break;
                    case ContactRelationKind.Child: Link(_children, r.Low, r.High); Link(_parents, r.High, r.Low); break;
                    case ContactRelationKind.Sibling: Link(_siblings, r.Low, r.High); Link(_siblings, r.High, r.Low); break;
                }
            }
        }

        public IReadOnlyCollection<Guid> Parents(Guid x) => Get(_parents, x);

        public IReadOnlyCollection<Guid> Children(Guid x) => Get(_children, x);

        // Co-children of x's parents (minus x) plus any explicit sibling edges.
        public IReadOnlyCollection<Guid> Siblings(Guid x)
        {
            var s = new HashSet<Guid>(Get(_siblings, x));
            foreach (var p in Get(_parents, x)) s.UnionWith(Get(_children, p));
            s.Remove(x);
            return s;
        }

        public IReadOnlyCollection<Guid> ExplicitPartners(Guid x) => Get(_partners, x);

        private static void Link(Dictionary<Guid, HashSet<Guid>> map, Guid from, Guid to)
        {
            if (!map.TryGetValue(from, out var set)) map[from] = set = [];
            set.Add(to);
        }

        private static IReadOnlyCollection<Guid> Get(Dictionary<Guid, HashSet<Guid>> map, Guid x) =>
            map.TryGetValue(x, out var set) ? set : [];
    }
}
