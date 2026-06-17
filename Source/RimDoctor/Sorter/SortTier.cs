using System;
using System.Collections.Generic;
using Verse;

namespace RimDoctor
{
    /// <summary>
    /// The canonical RimWorld load-order tiering, used as the TIE-BREAKER when no
    /// hard dependency or rule forces an order between two mods:
    ///
    ///   Harmony → Core → DLC → libraries/frameworks → races → content →
    ///   patches → textures → Combat Extended (very late) → pawn editors (last).
    ///
    /// Lower number = loads earlier. Classification is heuristic (name + packageId
    /// keywords + known ids) and only ever affects ordering among otherwise-equal
    /// mods, so a misclassification can't violate a real constraint.
    /// </summary>
    public enum Tier
    {
        Harmony = 0,
        Core = 10,
        Dlc = 20,
        Framework = 30,
        Race = 40,
        Content = 50,
        Patch = 60,
        Texture = 70,
        CombatExtended = 80,
        PawnEditor = 90
    }

    public static class SortTier
    {
        // Known absolute-last pawn editors.
        private static readonly HashSet<string> PawnEditors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "void.charactereditor", "edbprepare.carefully", "edbprepare.carefully.steam"
        };

        private static readonly HashSet<string> CombatExtended = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ceteam.combatextended", "ceteam.combatextended.steam"
        };

        public static Tier Of(ModMetaData mod)
        {
            try
            {
                string id = (mod.PackageId ?? "").ToLowerInvariant();
                string name = (mod.Name ?? "").ToLowerInvariant();

                if (id == "brrainz.harmony") return Tier.Harmony;
                if (mod.IsCoreMod) return Tier.Core;
                if (mod.Official) return Tier.Dlc; // Royalty/Ideology/Biotech/Anomaly/Odyssey

                if (CombatExtended.Contains(id) || name.Contains("combat extended")) return Tier.CombatExtended;
                if (PawnEditors.Contains(id) || name.Contains("character editor") || name.Contains("prepare carefully"))
                    return Tier.PawnEditor;

                if (Mentions(id, name, "framework", "library", "core", "lib", "hugslib", "xmlextensions", "vehicleframework"))
                    return Tier.Framework;
                if (Mentions(id, name, "race", "alienrace", "humanoidalien"))
                    return Tier.Race;
                if (Mentions(id, name, "patch", "compatibility", "patches"))
                    return Tier.Patch;
                if (Mentions(id, name, "texture", "retexture", "hd textures", "graphics"))
                    return Tier.Texture;

                return Tier.Content;
            }
            catch
            {
                return Tier.Content;
            }
        }

        private static bool Mentions(string id, string name, params string[] needles)
        {
            foreach (var n in needles)
                if (id.Contains(n) || name.Contains(n)) return true;
            return false;
        }

        public static string Describe(Tier t)
        {
            switch (t)
            {
                case Tier.Harmony: return "Harmony";
                case Tier.Core: return "Core";
                case Tier.Dlc: return "DLC";
                case Tier.Framework: return "library/framework";
                case Tier.Race: return "race";
                case Tier.Content: return "content";
                case Tier.Patch: return "patch";
                case Tier.Texture: return "texture";
                case Tier.CombatExtended: return "Combat Extended";
                case Tier.PawnEditor: return "pawn editor";
                default: return t.ToString();
            }
        }
    }
}
