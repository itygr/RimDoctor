using System;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// RimDoctor boot wiring. Split into:
    ///   • EnsureEarlyInit() — runs from the Mod constructor (as early as possible)
    ///     so log capture is live for the WHOLE load sequence, letting RimDoctor
    ///     record errors from OTHER mods as they load. Idempotent + fully guarded.
    ///   • the [StaticConstructorOnStartup] below — runs after defs load; re-ensures
    ///     init (belt-and-suspenders) and writes a "load completed" marker so that
    ///     if the game later fails, the log proves RimDoctor finished cleanly and
    ///     was not the cause.
    ///
    /// Every step is individually guarded: a failure disables only that step and
    /// never propagates into RimWorld's load.
    /// </summary>
    public static class RimDoctorStartup
    {
        private static bool earlyInitDone;
        private static bool unityHookRegistered;

        /// <summary>Idempotent early init — safe to call multiple times.</summary>
        public static void EnsureEarlyInit()
        {
            if (earlyInitDone) return;
            earlyInitDone = true;

            // Load rule data first so errors captured during load can be explained.
            try { LogAdviceDatabase.LoadOrReload(); }
            catch (Exception e) { RDLog.Exception("Loading advice DB failed", e); }

            try { CommunityRules.LoadOrReload(); }
            catch (Exception e) { RDLog.Exception("Loading community rules failed", e); }

            // Capture Unity-level messages that don't flow through Verse.Log
            // (e.g. "null texture passed to GUI.DrawTexture", native exceptions).
            // This callback can fire off the main thread — Capture() does no Unity work.
            try
            {
                if (!unityHookRegistered)
                {
                    Application.logMessageReceivedThreaded += OnUnityLog;
                    unityHookRegistered = true;
                    RDLog.Msg("Registered Unity log hook (early) for Log Doctor.");
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("Registering Unity log hook failed", e);
            }
        }

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    LogDoctor.Capture(LogSeverity.Error, condition, stackTrace);
                    break;
                case LogType.Warning:
                    LogDoctor.Capture(LogSeverity.Warning, condition, stackTrace);
                    break;
                // Ignore LogType.Log (normal messages) from Unity — too noisy.
            }
        }

        [StaticConstructorOnStartup]
        private static class PostLoad
        {
            static PostLoad()
            {
                try
                {
                    EnsureEarlyInit(); // in case the Mod ctor path didn't run for some reason
                    // Index sound paths now that defs are loaded, so sound-path texture
                    // probes get classified benign.
                    SoundPathIndex.Build();
                    ModAssemblyIndex.Build();          // for code-based culprit attribution
                    LogDoctor.ReclassifySoundProbes(); // sweep load-time probes into benign

                    // Performance attribution: map assemblies -> mods, time every
                    // component-tick override (always on), and restore opt-in per-thing
                    // timing if the player had it enabled.
                    TickAttribution.BuildAssemblyMap();
                    Patch_Perf_Components.PatchAll(RimDoctorMod.HarmonyInstance);
                    if (RimDoctorMod.Instance?.Settings?.detailedThingTiming == true)
                        Patch_Perf_ThingTick.Enable();

                    // Per-mod startup load weight (defs/assemblies + RimDoctor-witnessed span).
                    StartupAnalytics.Collect();
                    RDLog.Msg($"Load completed cleanly. Log Doctor: {LogAdviceDatabase.RuleCount} rule(s); " +
                              $"{LogDoctor.IssueCount} issue(s) to address, {LogDoctor.BenignCount} benign.");
                }
                catch (Exception e)
                {
                    RDLog.Exception("PostLoad marker failed", e);
                }
            }
        }
    }
}
