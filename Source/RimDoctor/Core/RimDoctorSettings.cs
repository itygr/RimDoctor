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

        // DEPRECATED / NO-OP since v1.0.4. Field kept only so existing config files
        // load without complaint. The old behaviour patched ContentFinder<Texture2D>.Get,
        // which on Mono also intercepted ContentFinder<AudioClip>.Get (generic code
        // sharing) and silently broke ALL audio. The patch has been removed; this
        // value is no longer read anywhere.
        public bool aggressiveLoadFallback = false;

        // ---- Milestone 4: Log Doctor ----
        public bool logDoctorEnabled = true;
        // Quarantine + skip known-benign vanilla log spam (song/audio probes, etc.)
        // from the game's dev log so the real, actionable errors stand out.
        public bool suppressBenignLogSpam = true;
        // Open RimDoctor's plain-language log window instead of RimWorld's raw one
        // when the red error box is clicked. OPT-IN (default off): on big mod packs
        // the old default auto-popped the window on every streamed error, which was
        // intrusive and interrupted audio. The Log Doctor is always available from the
        // RimDoctor tab regardless of this setting; this only changes the red-box click.
        public bool useRimDoctorLogWindow = false;

        // ---- Milestone 5: repair tier ----
        public RepairTier repairTier = RepairTier.ReportOnly;
        public int backupRetention = 10; // how many timestamped backup sets to keep

        // ---- Performance analytics (TPS + per-mod tick attribution) ----
        // Show the compact on-screen performance HUD (TPS/ms/FPS/top hog).
        public bool showPerfOverlay = false;
        // Opt-in: time Thing.DoTick per-mod. Accurate but a hot path (~5-15% tick tax);
        // when off, per-mod cost is shown via the free count proxy instead.
        public bool detailedThingTiming = false;
        // Persisted HUD position (top-left default when both 0).
        public float overlayX = 0f;
        public float overlayY = 0f;

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
            Scribe_Values.Look(ref useRimDoctorLogWindow, "useRimDoctorLogWindow", false);
            Scribe_Values.Look(ref showPerfOverlay, "showPerfOverlay", false);
            Scribe_Values.Look(ref detailedThingTiming, "detailedThingTiming", false);
            Scribe_Values.Look(ref overlayX, "overlayX", 0f);
            Scribe_Values.Look(ref overlayY, "overlayY", 0f);
            Scribe_Values.Look(ref repairTier, "repairTier", RepairTier.ReportOnly);
            Scribe_Values.Look(ref backupRetention, "backupRetention", 10);
            Scribe_Values.Look(ref communityRulesUrl, "communityRulesUrl",
                "https://raw.githubusercontent.com/RimSort/Community-Rules-DB/main/communityRules.json");
            base.ExposeData();
        }
    }
}
