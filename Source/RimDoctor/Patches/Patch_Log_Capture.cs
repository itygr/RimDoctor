using System;
using HarmonyLib;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Captures Verse.Log.Error / Warning / Message for the Log Doctor.
    ///
    /// Targets (confirmed against 1.6.4850):
    ///   Verse.Log.Error(string)
    ///   Verse.Log.Warning(string)
    ///   Verse.Log.Message(string)
    ///
    /// Postfix (not prefix) so we never interfere with the game's own logging —
    /// the message still prints normally; we just observe it.
    /// </summary>
    [HarmonyPatch(typeof(Log), nameof(Log.Error), new[] { typeof(string) })]
    public static class Patch_Log_Error
    {
        [HarmonyPostfix]
        public static void Postfix(string text) => LogDoctor.Capture(LogSeverity.Error, text);
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Warning), new[] { typeof(string) })]
    public static class Patch_Log_Warning
    {
        [HarmonyPostfix]
        public static void Postfix(string text) => LogDoctor.Capture(LogSeverity.Warning, text);
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Message), new[] { typeof(string) })]
    public static class Patch_Log_Message
    {
        [HarmonyPostfix]
        public static void Postfix(string text) => LogDoctor.Capture(LogSeverity.Message, text);
    }
}
