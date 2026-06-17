using System;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// One-time startup wiring that must run after the game has loaded mods and
    /// Unity is alive. [StaticConstructorOnStartup] runs after defs load — perfect
    /// for loading rule DBs and registering the Unity log hook.
    ///
    /// Everything here is guarded: a failure disables only the affected feature.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimDoctorStartup
    {
        static RimDoctorStartup()
        {
            // Load advice rules for the Log Doctor.
            try { LogAdviceDatabase.LoadOrReload(); }
            catch (Exception e) { RDLog.Exception("Loading advice DB failed", e); }

            // Load community sorting rules for the load-order sorter.
            try { CommunityRules.LoadOrReload(); }
            catch (Exception e) { RDLog.Exception("Loading community rules failed", e); }

            // Capture Unity-level messages that don't flow through Verse.Log
            // (e.g. "null texture passed to GUI.DrawTexture", native exceptions).
            // This callback can fire off the main thread — Capture() does no Unity work.
            try
            {
                Application.logMessageReceivedThreaded += OnUnityLog;
                RDLog.Msg("Registered Unity log hook for Log Doctor.");
            }
            catch (Exception e)
            {
                RDLog.Exception("Registering Unity log hook failed", e);
            }
        }

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            // Verse.Log routes through here too; the Verse.Log postfixes already
            // captured those. To avoid double-counting we only take messages that
            // Verse.Log does NOT emit: the raw Unity warnings/errors. Verse.Log
            // messages are tagged by RimWorld's own handler, but the cheapest
            // reliable filter is the dedup in LogDoctor.Capture — identical text
            // collapses to one entry regardless of source.
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
    }
}
