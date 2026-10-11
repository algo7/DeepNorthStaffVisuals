using System.Collections.Generic;

namespace WeaponAuras
{
    /// <summary>A hand-set correction for one weapon where the automatic fit looks wrong.</summary>
    internal sealed class Fix
    {
        /// <summary>Moves the effect along the weapon's length, as a fraction of it (0.1 = a tenth further to the tip).</summary>
        public double Along { get; set; }

        /// <summary>Moves the effect across the weapon's width, as a fraction of it.</summary>
        public double Across { get; set; }

        /// <summary>Extra stretch of the emitters (1 = as fitted).</summary>
        public double Stretch { get; set; } = 1;

        /// <summary>Borrow from this kind's donor instead of the weapon's own kind (<see cref="Kind.None"/> = no change).</summary>
        public Kind DonorKind { get; set; } = Kind.None;
    }

    /// <summary>The fix list. Unity-free.</summary>
    internal static class Fixes
    {
        /// <summary>
        /// Key: an item prefab name (every look) or "prefab/look" (that look only). Empty until a weapon is seen to need one.
        /// </summary>
        private static readonly Dictionary<string, Fix> s_fixes = new Dictionary<string, Fix>();

        /// <summary>The fix for this weapon and look, or null.</summary>
        public static Fix For(string prefab, string look) =>
            s_fixes.TryGetValue(prefab + "/" + look, out var fix) || s_fixes.TryGetValue(prefab, out fix) ? fix : null;
    }
}
