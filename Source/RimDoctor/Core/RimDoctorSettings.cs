using Verse;

namespace RimDoctor
{
    /// <summary>How aggressively RimDoctor is allowed to repair problems.</summary>
    public enum RepairTier
    {
        /// <summary>Only report problems; change nothing.</summary>
        ReportOnly = 0,
        /// <summary>Non-destructive fixes emitted into the generated override mod.</summary>
        SafeAutoFix = 1,
        /// <summary>Also rewrite/disable broken defs via override-mod PatchOperations.</summary>
        Maximum = 2
    }

    public class RimDoctorSettings : ModSettings
    {
        // ---- Milestone 3: runtime texture fallback (headline feature, on by default) ----
        public bool textureFallbackEnabled = true;
        public bool logEachSubstitution = true; // log each missing path once (deduped)

        // ---- Milestone 4: Log Doctor ----
        public bool logDoctorEnabled = true;

        // ---- Milestone 5: repair tier ----
        public RepairTier repairTier = RepairTier.ReportOnly;
        public int backupRetention = 10; // how many timestamped backup sets to keep

        // ---- Data refresh URLs (hot-reloadable rule DBs) ----
        public string communityRulesUrl =
            "https://raw.githubusercontent.com/RimSort/Community-Rules-DB/main/communityRules.json";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref textureFallbackEnabled, "textureFallbackEnabled", true);
            Scribe_Values.Look(ref logEachSubstitution, "logEachSubstitution", true);
            Scribe_Values.Look(ref logDoctorEnabled, "logDoctorEnabled", true);
            Scribe_Values.Look(ref repairTier, "repairTier", RepairTier.ReportOnly);
            Scribe_Values.Look(ref backupRetention, "backupRetention", 10);
            Scribe_Values.Look(ref communityRulesUrl, "communityRulesUrl",
                "https://raw.githubusercontent.com/RimSort/Community-Rules-DB/main/communityRules.json");
            base.ExposeData();
        }
    }
}
