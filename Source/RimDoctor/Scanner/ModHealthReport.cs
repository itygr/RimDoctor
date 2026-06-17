using System.Collections.Generic;

namespace RimDoctor
{
    public enum HealthStatus { Clean, MissingTextures, LikelyBroken }

    public class MissingTextureRef
    {
        public string defName;
        public string defType;
        public string texPath;
    }

    /// <summary>Health report for one mod.</summary>
    public class ModHealthReport
    {
        public string modName;
        public string packageId;
        public HealthStatus status = HealthStatus.Clean;
        public string note;                       // explanation for LikelyBroken
        public readonly List<MissingTextureRef> missing = new List<MissingTextureRef>();

        public int MissingCount => missing.Count;
    }

    /// <summary>Result of a full scan.</summary>
    public class HealthScanResult
    {
        public readonly List<ModHealthReport> reports = new List<ModHealthReport>();
        public int defsScanned;
        public int texturesChecked;
        public bool completed;

        public int CleanMods => reports.FindAll(r => r.status == HealthStatus.Clean).Count;
        public int MissingMods => reports.FindAll(r => r.status == HealthStatus.MissingTextures).Count;
        public int BrokenMods => reports.FindAll(r => r.status == HealthStatus.LikelyBroken).Count;
    }
}
