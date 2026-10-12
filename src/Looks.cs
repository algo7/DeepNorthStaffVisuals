using System;
using System.Collections.Generic;

namespace WeaponAuras
{
    /// <summary>
    /// The looks in key order, the game weapon each one is copied from (its donor) per kind, and which kinds stand in when
    /// a look has no donor of a weapon's own kind. Unity-free.
    /// </summary>
    internal static class Looks
    {
        public const string Plain = "Plain";

        /// <summary>The cycle after Plain, in order; each name is also the message shown.</summary>
        public static readonly string[] Order = { "Frostfire", "Thunderblood", "Bleeding", "Storming", "Primal", "Dyrnwyn", "Frostner" };

        private static readonly Dictionary<string, Dictionary<Kind, string>> s_donors = new Dictionary<string, Dictionary<Kind, string>>
        {
            ["Frostfire"] = DeepNorth("FrostFire"),
            ["Thunderblood"] = DeepNorth("BloodLightning"),
            ["Bleeding"] = Ashlands("SwordNiedhoggBlood", "THSwordSlayerBlood", "MaceEldnerBlood", "SpearSplitner_Blood",
                "BowAshlandsBlood", "CrossbowRipperBlood"),
            ["Storming"] = Ashlands("SwordNiedhoggLightning", "THSwordSlayerLightning", "MaceEldnerLightning",
                "SpearSplitner_Lightning", "BowAshlandsStorm", "CrossbowRipperLightning"),
            ["Primal"] = Ashlands("SwordNiedhoggNature", "THSwordSlayerNature", "MaceEldnerNature", "SpearSplitner_Nature",
                "BowAshlandsRoot", "CrossbowRipperNature"),
            ["Dyrnwyn"] = new Dictionary<Kind, string> { [Kind.Sword] = "SwordDyrnwyn" },
            ["Frostner"] = new Dictionary<Kind, string> { [Kind.Mace] = "MaceSilver" },
        };

        /// <summary>The kinds tried, in order, when a look has no donor of a weapon's own kind.</summary>
        private static readonly Dictionary<Kind, Kind[]> s_fallbacks = new Dictionary<Kind, Kind[]>
        {
            [Kind.Sword] = new[] { Kind.Greatsword, Kind.Knife },
            [Kind.Greatsword] = new[] { Kind.Sword },
            [Kind.Knife] = new[] { Kind.Sword },
            [Kind.Axe] = new[] { Kind.Battleaxe, Kind.Mace },
            [Kind.Battleaxe] = new[] { Kind.Axe, Kind.Sledge, Kind.Mace },
            [Kind.Mace] = new[] { Kind.Sledge },
            [Kind.Sledge] = new[] { Kind.Mace },
            [Kind.Spear] = new[] { Kind.Atgeir },
            [Kind.Atgeir] = new[] { Kind.Spear },
            [Kind.Staff] = new[] { Kind.Atgeir, Kind.Spear },
            [Kind.Bow] = new[] { Kind.Crossbow },
            [Kind.Crossbow] = new[] { Kind.Bow },
        };

        /// <summary>The Deep North gold weapons: one variant per kind, e.g. SwordGold_FrostFire.</summary>
        private static Dictionary<Kind, string> DeepNorth(string variant) => new Dictionary<Kind, string>
        {
            [Kind.Sword] = "SwordGold_" + variant,
            [Kind.Greatsword] = "THSwordGold_" + variant,
            [Kind.Knife] = "KnifeGold_" + variant,
            [Kind.Axe] = "AxeGold_" + variant,
            [Kind.Battleaxe] = "BattleaxeGold_" + variant,
            [Kind.Mace] = "MaceGold_" + variant,
            [Kind.Sledge] = "SledgeGold_" + variant,
            [Kind.Spear] = "SpearGold_" + variant,
            [Kind.Atgeir] = "AtgeirGold_" + variant,
            [Kind.Bow] = "BowGold_" + variant,
            [Kind.Crossbow] = "CrossbowGold_" + variant,
        };

        /// <summary>The Ashlands variants exist for these six kinds only.</summary>
        private static Dictionary<Kind, string> Ashlands(string sword, string greatsword, string mace, string spear, string bow,
            string crossbow) => new Dictionary<Kind, string>
        {
            [Kind.Sword] = sword,
            [Kind.Greatsword] = greatsword,
            [Kind.Mace] = mace,
            [Kind.Spear] = spear,
            [Kind.Bow] = bow,
            [Kind.Crossbow] = crossbow,
        };

        /// <summary>
        /// The donor prefab names to try for a look on a kind, best first: the kind's own donor, the fallback kinds' donors,
        /// then the look's other donors. Empty for Plain or an unknown look.
        /// </summary>
        public static List<string> DonorsFor(string look, Kind kind)
        {
            var names = new List<string>();
            if (look == null || !s_donors.TryGetValue(look, out var donors)) return names;
            void Add(Kind k)
            {
                if (donors.TryGetValue(k, out var name) && !names.Contains(name)) names.Add(name);
            }
            Add(kind);
            if (s_fallbacks.TryGetValue(kind, out var fallbacks))
                foreach (var k in fallbacks) Add(k);
            foreach (Kind k in Enum.GetValues(typeof(Kind))) Add(k);
            return names;
        }

        /// <summary>
        /// The donors' emitters that come off the weapon's own surface (flames, fire, clouds, spores, frost, Bleeding's
        /// drips), by name; " (1)"-style copies count as the same. Sparks, splats and flares keep their own shapes; Bleeding's
        /// swirls and the lightning repeat along the weapon instead (<see cref="SpreadsAlongWeapon"/>).
        /// </summary>
        private static readonly string[] s_followers =
            { "flames", "fx_Torch_Basic", "Burny vfx", "Clouds", "Spores", "Spores_World", "soft cloud", "vfx_BloodHit 1", "blood drip" };

        /// <summary>
        /// Emitters copied several times along the weapon (Fit.RingFractions): Bleeding's swirls circle the shaft around
        /// their own centre, so they can't start from the surface (they'd circle far from it); Storming's and Thunderblood's
        /// lightning is fired from a cone along the shaft and pulled in towards it.
        /// </summary>
        private static readonly string[] s_spreaders = { "Swirls", "Lightning" };

        public static bool FollowsWeapon(string emitterName) => Array.IndexOf(s_followers, BaseName(emitterName)) >= 0;

        public static bool SpreadsAlongWeapon(string emitterName) => Array.IndexOf(s_spreaders, BaseName(emitterName)) >= 0;

        /// <summary>"Swirls (1)" → "Swirls"; null or empty → "".</summary>
        private static string BaseName(string emitterName)
        {
            if (string.IsNullOrEmpty(emitterName)) return "";
            var copy = emitterName.LastIndexOf(" (", StringComparison.Ordinal);
            return copy > 0 && emitterName.EndsWith(")", StringComparison.Ordinal) ? emitterName.Substring(0, copy) : emitterName;
        }

        /// <summary>
        /// Extra particle rate for a look: Bleeding (swirls and drips only, no light; on the game's own weapons most of it
        /// is the red painted blade, which can't be copied) gets 2.5x so it shows. 1 for every other look.
        /// </summary>
        public static double RateBoost(string look) => look == "Bleeding" ? 2.5 : 1;

        /// <summary>
        /// The look after <paramref name="current"/> whose donors can be found (<paramref name="isAvailable"/>); after the
        /// last, or with none available, Plain. Plain or an unknown look starts from the first.
        /// </summary>
        public static string Next(string current, Func<string, bool> isAvailable)
        {
            for (var i = Array.IndexOf(Order, current) + 1; i < Order.Length; i++)
                if (isAvailable(Order[i])) return Order[i];
            return Plain;
        }
    }
}
