using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// Mirrors RimWorld's own texture-path resolution so the scanner doesn't
    /// false-positive on valid graphics:
    ///   • Graphic_Single   → one file at exactly "path"
    ///   • Graphic_Multi    → "path_south" / "_north" / "_east" / "_west"
    ///                         (south alone is valid; the rest are mirrored)
    ///   • Graphic_Random / Graphic_Collection → a FOLDER of variant files at "path"
    ///
    /// We use reportFailure:false everywhere — RimDoctor's M3 fallback deliberately
    /// leaves those probes returning null, so we see the real on-disk truth.
    ///
    /// A path counts as PRESENT if ANY resolution strategy finds something. This is
    /// intentionally conservative: better to miss a partial-rotation edge case than
    /// to wrongly flag thousands of valid multi/random graphics.
    /// </summary>
    public static class TextureResolver
    {
        private static readonly string[] DirSuffixes = { "_south", "_north", "_east", "_west" };

        public static bool Exists(string texPath)
        {
            if (string.IsNullOrEmpty(texPath))
                return true; // nothing to resolve; not our problem to flag

            try
            {
                if (Single(texPath)) return true;
                if (Multi(texPath)) return true;
                if (Folder(texPath)) return true;
                return false;
            }
            catch (Exception e)
            {
                RDLog.Exception($"TextureResolver.Exists('{texPath}') failed", e);
                return true; // on error, don't flag (fail safe — avoid false alarms)
            }
        }

        private static bool Single(string path)
        {
            return ContentFinder<Texture2D>.Get(path, reportFailure: false) != null;
        }

        private static bool Multi(string path)
        {
            foreach (var s in DirSuffixes)
                if (ContentFinder<Texture2D>.Get(path + s, reportFailure: false) != null)
                    return true;
            return false;
        }

        private static bool Folder(string path)
        {
            try
            {
                var all = ContentFinder<Texture2D>.GetAllInFolder(path);
                return all != null && all.Any();
            }
            catch
            {
                return false;
            }
        }
    }
}
