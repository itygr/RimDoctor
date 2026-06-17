using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimDoctor
{
    public class CompatIssue
    {
        public string modName;
        public string packageId;
        public string supportedVersions; // what it claims to support
        public bool unknown;             // no versions listed at all
    }

    /// <summary>
    /// Flags active mods whose About.xml does not list the current game version —
    /// the single most common cause of mystery breakage in large modlists. A mod
    /// built for 1.5 often half-loads on 1.6 and throws cryptic errors. Pure read of
    /// ModMetaData; changes nothing.
    /// </summary>
    public static class CompatibilityScanner
    {
        public static List<CompatIssue> Scan()
        {
            var issues = new List<CompatIssue>();
            try
            {
                int major = VersionControl.CurrentMajor;
                int minor = VersionControl.CurrentMinor;

                foreach (var mod in ModsConfig.ActiveModsInLoadOrder.Where(m => m != null))
                {
                    // Core/DLC are always in sync with the game.
                    if (mod.IsCoreMod || mod.Official) continue;

                    var versions = mod.SupportedVersionsReadOnly;
                    bool none = versions == null || versions.Count == 0;
                    bool supportsCurrent = !none && versions.Any(v => v.Major == major && v.Minor == minor);
                    if (supportsCurrent) continue;

                    issues.Add(new CompatIssue
                    {
                        modName = mod.Name,
                        packageId = mod.PackageId,
                        unknown = none,
                        supportedVersions = none
                            ? "(none listed)"
                            : string.Join(", ", versions.Select(v => v.Major + "." + v.Minor))
                    });
                }
            }
            catch (Exception e)
            {
                RDLog.Exception("CompatibilityScanner failed", e);
            }
            return issues;
        }

        public static string CurrentVersionLabel
        {
            get
            {
                try { return VersionControl.CurrentMajor + "." + VersionControl.CurrentMinor; }
                catch { return "current"; }
            }
        }
    }
}
