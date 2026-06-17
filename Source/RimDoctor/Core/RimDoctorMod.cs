using System;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// RimDoctor entry point. Holds settings, draws the settings window, and
    /// bootstraps Harmony.
    ///
    /// FAIL-SAFE CONTRACT: nothing in this class (or anything it calls during
    /// construction) may throw out to the game. A broken RimDoctor must still
    /// let RimWorld start. Every risky step is individually wrapped so a failure
    /// in one feature disables only that feature.
    /// </summary>
    public class RimDoctorMod : Mod
    {
        public static RimDoctorMod Instance { get; private set; }

        private RimDoctorSettings settings;
        public RimDoctorSettings Settings => settings;

        /// <summary>The single Harmony instance shared by all RimDoctor patches.</summary>
        public const string HarmonyId = "tyler.rimdoctor";
        public static Harmony HarmonyInstance { get; private set; }

        /// <summary>This mod's content pack — used to locate Data/ and the mod root on disk.</summary>
        public static ModContentPack ContentPack { get; private set; }

        public RimDoctorMod(ModContentPack content) : base(content)
        {
            Instance = this;
            ContentPack = content;

            // Start the persistent diagnostic log first so everything below is captured.
            try { DiagnosticLog.Init(); } catch { /* logger must never block load */ }

            // Settings load can fail on a corrupt file; never let that abort load.
            try
            {
                settings = GetSettings<RimDoctorSettings>();
            }
            catch (Exception e)
            {
                RDLog.Exception("Failed to load settings; using defaults", e);
                settings = new RimDoctorSettings();
            }

            // Patch each annotated class INDIVIDUALLY so a single failed target
            // (e.g. a hot Unity method that can't be patched) disables only that
            // one feature — never all of RimDoctor. If Harmony itself is missing,
            // we silently do nothing rather than crashing the game.
            try
            {
                HarmonyInstance = new Harmony(HarmonyId);
                int ok = 0, fail = 0;
                foreach (var type in typeof(RimDoctorMod).Assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                        continue;
                    try
                    {
                        HarmonyInstance.CreateClassProcessor(type).Patch();
                        ok++;
                    }
                    catch (Exception pe)
                    {
                        fail++;
                        RDLog.Exception($"Patch class '{type.Name}' failed (that feature disabled)", pe);
                    }
                }
                RDLog.Msg($"Harmony patches applied: {ok} ok, {fail} failed (id={HarmonyId}).");
            }
            catch (Exception e)
            {
                RDLog.Exception("Harmony bootstrap failed — RimDoctor patches disabled", e);
            }

            // Install log capture + rule data as early as possible so RimDoctor
            // records errors from the rest of the load sequence (other mods included).
            try { RimDoctorStartup.EnsureEarlyInit(); }
            catch (Exception e) { RDLog.Exception("Early init failed", e); }

            RDLog.Msg("Constructor completed.");
        }

        public override string SettingsCategory() => "RimDoctor";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            try
            {
                var l = new Listing_Standard();
                l.Begin(inRect);

                l.Label("RimDoctor.Settings.Header".TranslateSafe("Runtime protection"));
                l.GapLine();
                l.CheckboxLabeled(
                    "RimDoctor.Settings.TextureFallback".TranslateSafe("Prevent missing-texture crashes (texture fallback)"),
                    ref settings.textureFallbackEnabled,
                    "RimDoctor.Settings.TextureFallback.Tip".TranslateSafe(
                        "Substitutes a generated placeholder for any texture that fails to load, instead of returning null (which freezes/crashes screens). HEADLINE feature — recommended ON."));
                l.CheckboxLabeled(
                    "RimDoctor.Settings.LogSub".TranslateSafe("Log each substituted texture once"),
                    ref settings.logEachSubstitution);

                l.Gap();
                l.CheckboxLabeled(
                    "RimDoctor.Settings.LogDoctor".TranslateSafe("Enable Log Doctor (explain errors in plain language)"),
                    ref settings.logDoctorEnabled);

                l.Gap();
                l.Label("RimDoctor.Settings.RepairTier".TranslateSafe("Repair tier"));
                if (l.RadioButton("RimDoctor.Settings.Tier.Report".TranslateSafe("Report only — change nothing"),
                        settings.repairTier == RepairTier.ReportOnly))
                    settings.repairTier = RepairTier.ReportOnly;
                if (l.RadioButton("RimDoctor.Settings.Tier.Safe".TranslateSafe("Safe auto-fix — non-destructive (via override mod)"),
                        settings.repairTier == RepairTier.SafeAutoFix))
                    settings.repairTier = RepairTier.SafeAutoFix;
                if (l.RadioButton("RimDoctor.Settings.Tier.Max".TranslateSafe("Maximum — rewrite/disable broken defs (via override mod)"),
                        settings.repairTier == RepairTier.Maximum))
                    settings.repairTier = RepairTier.Maximum;

                l.Gap();
                l.Label("RimDoctor.Settings.Backups".TranslateSafe(
                    $"Backup sets to keep: {settings.backupRetention}"));
                settings.backupRetention = (int)l.Slider(settings.backupRetention, 1, 50);

                l.Gap();
                if (l.ButtonText("RimDoctor.Settings.ReloadData".TranslateSafe("Reload rule data files (logAdvice + communityRules)")))
                {
                    LogAdviceDatabase.LoadOrReload();
                    CommunityRules.LoadOrReload();
                    Messages.Message("RimDoctor.Settings.DataReloaded".TranslateSafe(
                        $"Reloaded {LogAdviceDatabase.RuleCount} advice rule(s) and {CommunityRules.RuleCount} community rule(s)."),
                        MessageTypeDefOf.TaskCompletion, false);
                }

                l.End();
            }
            catch (Exception e)
            {
                RDLog.Exception("Settings window render failed", e);
            }
        }
    }
}
