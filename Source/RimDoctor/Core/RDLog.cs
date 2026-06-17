using System;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Centralized logging for RimDoctor. Every message is prefixed so it is easy
    /// to find (and filter) in the live log and in the Log Doctor's own capture.
    /// Each message is ALSO mirrored into the persistent DiagnosticLog so the
    /// Diagnostics panel + on-disk session log capture everything RimDoctor does.
    ///
    /// IMPORTANT: RimDoctor patches Verse.Log in Milestone 4. To avoid infinite
    /// recursion (our log handler matching our own messages), the Log Doctor
    /// capture explicitly ignores anything carrying the <see cref="Prefix"/>.
    /// </summary>
    public static class RDLog
    {
        public const string Prefix = "[RimDoctor]";

        public static void Msg(string message)
        {
            Log.Message($"{Prefix} {message}");
            DiagnosticLog.Info("General", message);
        }

        public static void Warn(string message)
        {
            Log.Warning($"{Prefix} {message}");
            DiagnosticLog.Warn("General", message);
        }

        public static void Error(string message)
        {
            Log.Error($"{Prefix} {message}");
            DiagnosticLog.Error("General", message);
        }

        /// <summary>
        /// Log an exception without ever rethrowing — RimDoctor must never take the
        /// game down. Use this in every patch/feature catch block.
        /// </summary>
        public static void Exception(string context, Exception e)
        {
            Log.Error($"{Prefix} {context}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            DiagnosticLog.Error("Exception", $"{context}: {e.GetType().Name}: {e.Message}");
        }
    }
}
