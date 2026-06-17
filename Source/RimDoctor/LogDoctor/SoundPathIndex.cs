using System;
using System.Collections.Generic;
using Verse;
using Verse.Sound;

namespace RimDoctor
{
    /// <summary>
    /// Index of every audio clip/folder path referenced by SoundDefs. RimWorld's
    /// content system probes ContentFinder&lt;Texture2D&gt; for these sound paths
    /// during load and logs "Could not load Texture2D at &lt;soundpath&gt;" — pure
    /// engine noise (sounds have no textures). We use this index to recognise those
    /// probes precisely (by exact path or folder prefix) instead of guessing from
    /// the directory name, so they can be quarantined as benign.
    ///
    /// Built once after defs load. Read-only afterwards.
    /// </summary>
    public static class SoundPathIndex
    {
        private static readonly HashSet<string> clipPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> folderPaths = new List<string>();
        public static bool Ready { get; private set; }
        public static int Count => clipPaths.Count + folderPaths.Count;

        public static void Build()
        {
            try
            {
                clipPaths.Clear();
                folderPaths.Clear();
                var defs = DefDatabase<SoundDef>.AllDefsListForReading;
                if (defs == null) { Ready = true; return; }

                foreach (var sd in defs)
                {
                    if (sd?.subSounds == null) continue;
                    foreach (var ss in sd.subSounds)
                    {
                        if (ss?.grains == null) continue;
                        foreach (var grain in ss.grains)
                        {
                            if (grain is AudioGrain_Clip clip && !string.IsNullOrEmpty(clip.clipPath))
                                clipPaths.Add(clip.clipPath);
                            else if (grain is AudioGrain_Folder folder && !string.IsNullOrEmpty(folder.clipFolderPath))
                                folderPaths.Add(folder.clipFolderPath.TrimEnd('/') + "/");
                        }
                    }
                }
                Ready = true;
                RDLog.Msg($"Indexed {clipPaths.Count} sound clip path(s) + {folderPaths.Count} folder(s) for benign log classification.");
            }
            catch (Exception e)
            {
                RDLog.Exception("SoundPathIndex.Build failed", e);
                Ready = true; // don't retry forever; just won't classify these as benign
            }
        }

        /// <summary>True if the given path is a known SoundDef clip/folder path.</summary>
        public static bool IsSoundPath(string path)
        {
            if (string.IsNullOrEmpty(path) || clipPaths.Count == 0 && folderPaths.Count == 0)
                return false;
            if (clipPaths.Contains(path))
                return true;
            for (int i = 0; i < folderPaths.Count; i++)
                if (path.StartsWith(folderPaths[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
