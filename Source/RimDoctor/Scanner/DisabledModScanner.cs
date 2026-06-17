using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Detects active C# mods whose assembly failed to load: they ship a DLL for the
    /// CURRENT game version but RimWorld loaded no assembly from them. That mod is
    /// silently inert — its features just don't work, often with an error buried at
    /// startup. We only check mods that claim to support the current version (so a
    /// 1.5-only mod isn't double-flagged — that's the compatibility checker's job).
    /// </summary>
    public static class DisabledModScanner
    {
        public class DisabledMod
        {
            public string modName;
            public string packageId;
        }

        public static List<DisabledMod> Scan()
        {
            var result = new List<DisabledMod>();
            try
            {
                int major = VersionControl.CurrentMajor, minor = VersionControl.CurrentMinor;
                string verFolder = major + "." + minor;

                // Iterate ModContentPacks (they expose loaded assemblies + RootDir as string).
                foreach (var mod in LoadedModManager.RunningModsListForReading)
                {
                    if (mod == null || mod.IsCoreMod || mod.IsOfficialMod) continue;

                    // Skip mods that don't even claim current-version support.
                    var vers = mod.ModMetaData?.SupportedVersionsReadOnly;
                    bool claimsCurrent = vers != null && vers.Any(v => v.Major == major && v.Minor == minor);
                    if (!claimsCurrent) continue;

                    string root = mod.RootDir;
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                    bool hasCurrentDll = DllExistsIn(Path.Combine(root, "Assemblies"))
                                        || DllExistsIn(Path.Combine(Path.Combine(root, verFolder), "Assemblies"));
                    if (!hasCurrentDll) continue; // not a C# mod for this version

                    int loaded = mod.assemblies?.loadedAssemblies?.Count ?? 0;
                    if (loaded == 0)
                        result.Add(new DisabledMod { modName = mod.Name, packageId = mod.PackageId });
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("DisabledModScanner failed", e);
            }
            return result;
        }

        private static bool DllExistsIn(string dir)
        {
            try { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.dll").Any(); }
            catch { return false; }
        }
    }
}
