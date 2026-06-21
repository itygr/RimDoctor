using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimDoctor
{
    /// <summary>
    /// Save / world "bloat" analytics: the in-memory contents that make a save big and
    /// slow — total things (by mod), items/filth/plants, world pawns (by race-mod),
    /// maps and factions — plus the on-disk size of the most recent save file. Computed
    /// on demand (throttled) from the live game, so it reflects the running colony.
    /// </summary>
    public static class SaveAnalytics
    {
        public class ModCount { public string mod; public int count; }

        public static int TotalThings, Items, Filth, Plants, Buildings, Pawns, WorldPawns, Maps, Factions;
        public static long SaveFileBytes;
        public static string SaveFileName = "";
        public static List<ModCount> ThingsByMod = new List<ModCount>();
        public static List<ModCount> WorldPawnsByMod = new List<ModCount>();
        public static bool Ready;

        public static void Collect()
        {
            try
            {
                TotalThings = Items = Filth = Plants = Buildings = Pawns = 0;
                var thingsByMod = new Dictionary<string, int>();

                var maps = Find.Maps;
                Maps = maps?.Count ?? 0;
                if (maps != null)
                    foreach (var map in maps)
                    {
                        var all = map?.listerThings?.AllThings;
                        if (all == null) continue;
                        for (int i = 0; i < all.Count; i++)
                        {
                            var t = all[i];
                            if (t == null) continue;
                            TotalThings++;
                            switch (t.def?.category)
                            {
                                case ThingCategory.Item: Items++; break;
                                case ThingCategory.Filth: Filth++; break;
                                case ThingCategory.Plant: Plants++; break;
                                case ThingCategory.Building: Buildings++; break;
                                case ThingCategory.Pawn: Pawns++; break;
                            }
                            var mcp = t.def?.modContentPack;
                            if (mcp != null && !mcp.IsCoreMod && !mcp.IsOfficialMod)
                            {
                                string n = mcp.Name ?? "?";
                                thingsByMod.TryGetValue(n, out var c);
                                thingsByMod[n] = c + 1;
                            }
                        }
                    }
                ThingsByMod = Top(thingsByMod, 10);

                var wp = Find.WorldPawns?.AllPawnsAliveOrDead;
                WorldPawns = wp?.Count ?? 0;
                var wpByMod = new Dictionary<string, int>();
                if (wp != null)
                    foreach (var p in wp)
                    {
                        var mcp = p?.def?.modContentPack;
                        string n = (mcp != null && !mcp.IsCoreMod && !mcp.IsOfficialMod) ? (mcp.Name ?? "?") : "Vanilla";
                        wpByMod.TryGetValue(n, out var c);
                        wpByMod[n] = c + 1;
                    }
                WorldPawnsByMod = Top(wpByMod, 10);

                Factions = Find.FactionManager?.AllFactionsListForReading?.Count ?? 0;

                // size of the most recently written save file (best-effort hint)
                SaveFileBytes = 0; SaveFileName = "";
                try
                {
                    string dir = GenFilePaths.SavedGamesFolderPath;
                    if (Directory.Exists(dir))
                    {
                        string newest = null; DateTime best = DateTime.MinValue;
                        foreach (var f in Directory.GetFiles(dir, "*.rws"))
                        {
                            var t = File.GetLastWriteTime(f);
                            if (t > best) { best = t; newest = f; }
                        }
                        if (newest != null)
                        {
                            SaveFileBytes = new FileInfo(newest).Length;
                            SaveFileName = Path.GetFileNameWithoutExtension(newest);
                        }
                    }
                }
                catch { }

                Ready = true;
            }
            catch (Exception e) { RDLog.Exception("SaveAnalytics.Collect failed", e); }
        }

        private static List<ModCount> Top(Dictionary<string, int> d, int n)
        {
            var list = new List<ModCount>(d.Count);
            foreach (var kv in d) list.Add(new ModCount { mod = kv.Key, count = kv.Value });
            list.Sort((a, b) => b.count.CompareTo(a.count));
            return list.Count > n ? list.GetRange(0, n) : list;
        }
    }
}
