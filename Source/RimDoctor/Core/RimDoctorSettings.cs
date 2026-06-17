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
        // Safe, draw-layer protection: substitute a placeholder only when a null
        // texture is actually about to be DRAWN (GUI.DrawTexture). This cannot break
        // load-time null handling and only runs on the main thread.
        public bool textureFallbackEnabled = true;
        public bool logEachSubstitution = true; // log each missing path once (deduped)

        // DANGEROUS, OFF by default: also substitute at ContentFinder<Texture2D>.Get
        // (load time). RimWorld + many mods call Get expecting null and handle it;
        // handing back a placeholder breaks that logic and can crash on load. Only
        // enable if you understand the risk. The draw-layer fallback above is the
        // real anti-freeze protection; this is almost never needed.
        public bool aggressiveLoadFallback = false;

        // ---- Milestone 4: Log Doctor ----
        public bool logDoctorEnabled = true;
        // Quarantine + skip known-benign vanilla log spam (song/audio probes, etc.)
        // from the game's dev log so the real, actionable errors stand out.
        public bool suppressBenignLogSpam = true;
        // Open RimDoctor's plain-language log window instead of RimWorld's raw one
        // when the red error box is clicked / the log auto-opens.
        public bool useRimDoctorLogWindow = true;

        // ---- Milestone 5: repair tier ----
        public RepairTier repairTier = RepairTier.ReportOnly;
        public int backupRetention = 10; // how many timestamped backup sets to keep

        // ---- Data refresh URLs (hot-reloadable rule DBs) ----
        public string communityRulesUrl =
            "https://raw.githubusercontent.com/RimSort/Community-Rules-DB/main/communityRules.json";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref textureFallbackEnabled, "textureFallbackEnabled", true);
            Scribe_Values.Look(ref aggressiveLoadFallback, "aggressiveLoadFallback", false);
            Scribe_Values.Look(ref logEachSubstitution, "logEachSubstitution", true);
            Scribe_Values.Look(ref logDoctorEnabled, "logDoctorEnabled", true);
            Scribe_Values.Look(ref suppressBenignLogSpam, "suppressBenignLogSpam", true);
            Scribe_Values.Look(ref useRimDoctorLogWindow, "useRimDoctorLogWindow", true);
            Scribe_Values.Look(ref repairTier, "repairTier", RepairTier.ReportOnly);
            Scribe_Values.Look(ref backupRetention, "backupRetention", 10);
            Scribe_Values.Look(ref communityRulesUrl, "communityRulesUrl",
                "https://raw.githubusercontent.com/RimSort/Community-Rules-DB/main/communityRules.json");
            base.ExposeData();
        }
    }
}
