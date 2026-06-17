using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Builds a dependency graph from active mods and topologically sorts it, using
    /// the canonical tier as the tie-breaker. Detects cycles + surfaces warnings.
    ///
    /// Constraint precedence (highest first) — also drives the "why placed" reason:
    ///   hard About.xml dependency  >  forceLoadAfter/Before  >
    ///   About.xml loadAfter/Before >  community rule  >  tier heuristic
    /// </summary>
    public static class LoadOrderSorter
    {
        /// <summary>The most recent sort result, for the Diagnostics aggregator to read.</summary>
        public static SortResult Last { get; private set; }

        private enum EdgeReason { Tier = 0, Community = 1, About = 2, Force = 3, Dependency = 4 }

        private class Node
        {
            public ModMetaData mod;
            public string id;          // lowercased PackageId
            public int originalIndex;  // position in current active order
            public Tier tier;
            public readonly List<Node> successors = new List<Node>(); // this -> succ (this before succ)
            public int inDegree;
            // best incoming edge (predecessor + why), for the placement reason
            public Node bestPred;
            public EdgeReason bestPredReason = EdgeReason.Tier;
        }

        public static SortResult Sort()
        {
            var result = new SortResult();
            try
            {
                var active = ModsConfig.ActiveModsInLoadOrder.Where(m => m != null).ToList();

                // Build nodes + an id lookup that accepts PackageId and PackageIdNonUnique.
                var nodes = new List<Node>();
                var lookup = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < active.Count; i++)
                {
                    var m = active[i];
                    var node = new Node
                    {
                        mod = m,
                        id = (m.PackageId ?? "").ToLowerInvariant(),
                        originalIndex = i,
                        tier = SortTier.Of(m)
                    };
                    nodes.Add(node);
                    result.currentPackageIds.Add(m.PackageId);
                    Register(lookup, node.id, node);
                    Register(lookup, (m.PackageIdNonUnique ?? "").ToLowerInvariant(), node);
                }

                // Add edges from each mod's metadata + community rules.
                foreach (var node in nodes)
                    AddConstraintEdges(node, lookup, result);

                // Kahn's algorithm with (tier, originalIndex) tie-break.
                var available = new List<Node>(nodes.Where(n => n.inDegree == 0));
                var placed = new List<Node>(nodes.Count);
                var placedSet = new HashSet<Node>();

                while (available.Count > 0)
                {
                    available.Sort(CompareForPick);
                    var pick = available[0];
                    available.RemoveAt(0);
                    placed.Add(pick);
                    placedSet.Add(pick);

                    foreach (var succ in pick.successors)
                    {
                        if (placedSet.Contains(succ)) continue;
                        succ.inDegree--;
                        if (succ.inDegree == 0)
                            available.Add(succ);
                    }
                }

                // Any node not placed is part of a cycle.
                if (placed.Count < nodes.Count)
                {
                    result.hasCycle = true;
                    var stuck = nodes.Where(n => !placedSet.Contains(n)).ToList();
                    var names = string.Join(", ", stuck.Take(12).Select(n => n.mod.Name));
                    result.warnings.Add(new SortWarning
                    {
                        kind = WarningKind.Cycle,
                        text = $"Dependency cycle detected among {stuck.Count} mod(s): {names}" +
                               (stuck.Count > 12 ? " …" : "") +
                               ". They were appended in their current order — resolve the conflicting loadAfter/loadBefore rules."
                    });
                    // Append stuck nodes in original order so we still produce a usable list.
                    foreach (var n in stuck.OrderBy(n => n.originalIndex))
                        placed.Add(n);
                }

                foreach (var n in placed)
                {
                    result.proposedOrder.Add(n.mod);
                    result.proposedPackageIds.Add(n.mod.PackageId);
                    result.reasons[n.mod.PackageId] = BuildReason(n);
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("Load-order sort failed", e);
            }
            Last = result;
            return result;
        }

        private static void Register(Dictionary<string, Node> lookup, string key, Node node)
        {
            if (!string.IsNullOrEmpty(key) && !lookup.ContainsKey(key))
                lookup[key] = node;
        }

        private static void AddConstraintEdges(Node node, Dictionary<string, Node> lookup, SortResult result)
        {
            var m = node.mod;

            // Hard dependencies (must be active + load before).
            if (m.Dependencies != null)
            {
                foreach (var dep in m.Dependencies)
                {
                    string depId = (dep?.packageId ?? "").ToLowerInvariant();
                    if (string.IsNullOrEmpty(depId)) continue;
                    if (lookup.TryGetValue(depId, out var depNode))
                        AddEdge(depNode, node, EdgeReason.Dependency);
                    else
                        result.warnings.Add(new SortWarning
                        {
                            kind = WarningKind.MissingDependency,
                            text = $"{m.Name} requires '{depId}', which is not active. Subscribe/enable it."
                        });
                }
            }

            // loadAfter(X) means THIS loads after X ⇒ X before THIS ⇒ edge X->THIS.
            AddPredecessors(m.ForceLoadAfter, node, lookup, EdgeReason.Force);
            AddSuccessors(m.ForceLoadBefore, node, lookup, EdgeReason.Force);
            AddPredecessors(m.LoadAfter, node, lookup, EdgeReason.About);
            AddSuccessors(m.LoadBefore, node, lookup, EdgeReason.About);

            // Incompatibilities → warning only.
            if (m.IncompatibleWith != null)
                foreach (var bad in m.IncompatibleWith)
                {
                    string badId = (bad ?? "").ToLowerInvariant();
                    if (lookup.ContainsKey(badId))
                        result.warnings.Add(new SortWarning
                        {
                            kind = WarningKind.Incompatible,
                            text = $"{m.Name} is marked incompatible with '{badId}', which is also active."
                        });
                }

            // Community rules.
            var rule = CommunityRules.For(node.id);
            if (rule != null)
            {
                AddPredecessors(rule.loadAfter, node, lookup, EdgeReason.Community);
                AddSuccessors(rule.loadBefore, node, lookup, EdgeReason.Community);
            }
        }

        // "this loads after X" ⇒ X is a predecessor ⇒ edge X -> this.
        private static void AddPredecessors(List<string> ids, Node node, Dictionary<string, Node> lookup, EdgeReason reason)
        {
            if (ids == null) return;
            foreach (var raw in ids)
            {
                if (lookup.TryGetValue((raw ?? "").ToLowerInvariant(), out var pred) && pred != node)
                    AddEdge(pred, node, reason);
            }
        }

        // "this loads before X" ⇒ edge this -> X.
        private static void AddSuccessors(List<string> ids, Node node, Dictionary<string, Node> lookup, EdgeReason reason)
        {
            if (ids == null) return;
            foreach (var raw in ids)
            {
                if (lookup.TryGetValue((raw ?? "").ToLowerInvariant(), out var succ) && succ != node)
                    AddEdge(node, succ, reason);
            }
        }

        private static void AddEdge(Node from, Node to, EdgeReason reason)
        {
            if (from == to) return;
            if (!from.successors.Contains(to))
            {
                from.successors.Add(to);
                to.inDegree++;
            }
            // Track the strongest reason for 'to' coming after a predecessor.
            if (reason >= to.bestPredReason)
            {
                to.bestPredReason = reason;
                to.bestPred = from;
            }
        }

        private static int CompareForPick(Node a, Node b)
        {
            int t = ((int)a.tier).CompareTo((int)b.tier);
            if (t != 0) return t;
            return a.originalIndex.CompareTo(b.originalIndex);
        }

        private static string BuildReason(Node n)
        {
            if (n.bestPred != null)
            {
                string why;
                switch (n.bestPredReason)
                {
                    case EdgeReason.Dependency: why = "hard dependency"; break;
                    case EdgeReason.Force: why = "forced load order"; break;
                    case EdgeReason.About: why = "About.xml rule"; break;
                    case EdgeReason.Community: why = "community rule"; break;
                    default: why = "tier"; break;
                }
                return $"after \"{n.bestPred.mod.Name}\" ({why})";
            }
            return $"tier: {SortTier.Describe(n.tier)}";
        }
    }
}
