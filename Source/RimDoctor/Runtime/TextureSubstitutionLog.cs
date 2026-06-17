using System.Collections.Generic;

namespace RimDoctor
{
    /// <summary>
    /// Session-wide record of every texture path RimDoctor had to substitute a
    /// placeholder for. Deduped so each unique path logs exactly once (the same
    /// missing texture can be requested thousands of times per second). The
    /// Content Health scanner (M2) and Log Doctor (M4) read this to show "these
    /// textures were missing and got a placeholder this session."
    /// </summary>
    public static class TextureSubstitutionLog
    {
        public class Entry
        {
            public string path;
            public int hits;      // how many times this path was requested-and-missing
        }

        private static readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>();

        // Count of null-texture catches at the GUI.DrawTexture layer (no path available there).
        private static int drawTimeCatches;

        /// <summary>
        /// Record a missing path. Returns true the FIRST time a given path is seen
        /// (so callers can log-once); false on subsequent hits.
        /// </summary>
        public static bool RecordPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                path = "(unknown)";

            if (entries.TryGetValue(path, out var e))
            {
                e.hits++;
                return false;
            }
            entries[path] = new Entry { path = path, hits = 1 };
            return true;
        }

        /// <summary>Record a null-texture catch at draw time. Returns true on the first catch only.</summary>
        public static bool RecordDrawTimeCatch()
        {
            drawTimeCatches++;
            return drawTimeCatches == 1;
        }

        public static int DrawTimeCatchCount => drawTimeCatches;
        public static int UniquePathCount => entries.Count;

        public static IReadOnlyCollection<Entry> Entries => entries.Values;

        public static void Clear()
        {
            entries.Clear();
            drawTimeCatches = 0;
        }
    }
}
