using System.Collections.Generic;
using Verse;

namespace RimDoctor
{
    public enum WarningKind { MissingDependency, Incompatible, Cycle }

    public class SortWarning
    {
        public WarningKind kind;
        public string text;
    }

    public class SortResult
    {
        /// <summary>Proposed active order (packageIds), ready for ModsConfig.SetActiveToList.</summary>
        public readonly List<string> proposedPackageIds = new List<string>();
        /// <summary>Same order as ModMetaData for display.</summary>
        public readonly List<ModMetaData> proposedOrder = new List<ModMetaData>();
        /// <summary>The current order (packageIds) before sorting, for diffing.</summary>
        public readonly List<string> currentPackageIds = new List<string>();

        public readonly List<SortWarning> warnings = new List<SortWarning>();
        // packageId -> human reason for its placement
        public readonly Dictionary<string, string> reasons = new Dictionary<string, string>();

        public bool hasCycle;
        public bool Changed => !SameOrder(currentPackageIds, proposedPackageIds);

        public string ReasonFor(string packageId)
        {
            if (packageId != null && reasons.TryGetValue(packageId, out var r)) return r;
            return null;
        }

        private static bool SameOrder(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
