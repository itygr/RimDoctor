using System;
using HarmonyLib;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Captures Verse.Log.Error / Warning / Message for the Log Doctor, and — when
    /// 'suppressBenignLogSpam' is on — skips the original log call for messages that
    /// match a BENIGN advice rule, keeping the game's dev log clean.
    ///
    /// Targets (confirmed against 1.6.4850):
    ///   Verse.Log.Error(string) / Warning(string) / Message(string)
    ///
    /// Prefix returns false ONLY to drop a known-benign message; on any error it
    /// returns true so the game logs normally (RimDoctor never hides a real error).
    /// </summary>
    internal static class LogCaptureCore
    {
        public static bool HandleReturnRunOriginal(LogSeverity severity, string text)
        {
            try
            {
                bool benign = LogDoctor.Capture(severity, text);
                if (!benign)
                    return true;

                var s = RimDoctorMod.Instance?.Settings;
                if (s != null && s.suppressBenignLogSpam)
                    return false; // drop this benign message from the game's log
            }
            catch (Exception e)
            {
                RDLog.Exception("Log capture/suppress failed", e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Error), new[] { typeof(string) })]
    public static class Patch_Log_Error
    {
        [HarmonyPrefix]
        public static bool Prefix(string text) =>
            LogCaptureCore.HandleReturnRunOriginal(LogSeverity.Error, text);
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Warning), new[] { typeof(string) })]
    public static class Patch_Log_Warning
    {
        [HarmonyPrefix]
        public static bool Prefix(string text) =>
            LogCaptureCore.HandleReturnRunOriginal(LogSeverity.Warning, text);
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Message), new[] { typeof(string) })]
    public static class Patch_Log_Message
    {
        [HarmonyPrefix]
        public static bool Prefix(string text) =>
            LogCaptureCore.HandleReturnRunOriginal(LogSeverity.Message, text);
    }
}
