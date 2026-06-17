using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDoctor
{
    /// <summary>Collects environment facts for the diagnostic report header.</summary>
    public static class EnvironmentInfo
    {
        public static List<KeyValuePair<string, string>> Collect()
        {
            var kv = new List<KeyValuePair<string, string>>();
            void Add(string k, string v) => kv.Add(new KeyValuePair<string, string>(k, v));
            try
            {
                Add("Game version", VersionControl.CurrentVersionStringWithRev);
                Add("RimDoctor", "active");
                Add("OS", SystemInfo.operatingSystem);

                var active = ModsConfig.ActiveModsInLoadOrder?.Where(m => m != null).ToList()
                             ?? new List<ModMetaData>();
                Add("Active mods", active.Count.ToString());

                var dlcs = new List<string>();
                if (ModsConfig.RoyaltyActive) dlcs.Add("Royalty");
                if (ModsConfig.IdeologyActive) dlcs.Add("Ideology");
                if (ModsConfig.BiotechActive) dlcs.Add("Biotech");
                if (ModsConfig.AnomalyActive) dlcs.Add("Anomaly");
                if (ModsConfig.OdysseyActive) dlcs.Add("Odyssey");
                Add("DLC active", dlcs.Count > 0 ? string.Join(", ", dlcs) : "(none)");
            }
            catch (Exception e)
            {
                RDLog.Exception("EnvironmentInfo.Collect failed", e);
            }
            return kv;
        }

        public static string AsText()
        {
            var sb = new StringBuilder();
            foreach (var kv in Collect())
                sb.AppendLine($"- **{kv.Key}**: {kv.Value}");
            return sb.ToString();
        }
    }
}
