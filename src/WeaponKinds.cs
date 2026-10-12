using System;

namespace WeaponAuras
{
    /// <summary>The kinds of weapon a look can go on; each look has a donor weapon per kind (see <see cref="Looks"/>).</summary>
    internal enum Kind
    {
        None,
        Sword,
        Greatsword,
        Knife,
        Axe,
        Battleaxe,
        Mace,
        Sledge,
        Spear,
        Atgeir,
        Bow,
        Crossbow,
        Staff,
    }

    /// <summary>A weapon's kind from its item data. Unity-free.</summary>
    internal static class WeaponKinds
    {
        /// <summary>Drinking horns count as one-handed swords in the game data; they aren't weapons.</summary>
        private static readonly string[] s_notWeapons = { "Tankard", "TankardAnniversary", "TankardOdin", "Tankard_dvergr" };

        /// <summary>The kind, or <see cref="Kind.None"/> for anything that isn't a melee weapon, bow, crossbow or staff.</summary>
        public static Kind KindOf(ItemDrop.ItemData.ItemType type, Skills.SkillType skill, string prefabName)
        {
            if (Array.IndexOf(s_notWeapons, prefabName) >= 0) return Kind.None;
            if (type != ItemDrop.ItemData.ItemType.OneHandedWeapon && type != ItemDrop.ItemData.ItemType.TwoHandedWeapon
                && type != ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft && type != ItemDrop.ItemData.ItemType.Bow)
                return Kind.None;
            var twoHanded = type == ItemDrop.ItemData.ItemType.TwoHandedWeapon;
            switch (skill)
            {
                case Skills.SkillType.Swords: return twoHanded ? Kind.Greatsword : Kind.Sword;
                case Skills.SkillType.Knives: return Kind.Knife;
                case Skills.SkillType.Clubs: return twoHanded ? Kind.Sledge : Kind.Mace;
                case Skills.SkillType.Polearms: return Kind.Atgeir;
                case Skills.SkillType.Spears: return Kind.Spear;
                case Skills.SkillType.Axes: return twoHanded ? Kind.Battleaxe : Kind.Axe;
                case Skills.SkillType.Bows: return Kind.Bow;
                case Skills.SkillType.Crossbows: return Kind.Crossbow;
                case Skills.SkillType.ElementalMagic:
                case Skills.SkillType.BloodMagic: return Kind.Staff;
                default: return Kind.None;
            }
        }

        /// <summary>
        /// Only staffs take looks: glowing effects are what the game gives late-game weapons, and staffs are late-game gear
        /// that look plain. The other kinds still matter as donors (the variants the effects are copied from).
        /// </summary>
        public static bool CanTakeLook(Kind kind) => kind == Kind.Staff;

        /// <summary>
        /// Which way a kind's head or tip points along the model's length axis (z for every weapon): +1, except spears,
        /// whose heads point to -z (they're held near the middle, so the farther end isn't the head).
        /// </summary>
        public static int HeadSign(Kind kind) => kind == Kind.Spear ? -1 : 1;

        /// <summary>
        /// Where a kind's business end starts (blade, head, limbs), as a fraction of its length from the butt towards the
        /// head: our looks are fitted between here and the tip. Bows: the whole bow.
        /// </summary>
        public static double EffectStart(Kind kind)
        {
            switch (kind)
            {
                case Kind.Sword: return 0.21;
                case Kind.Greatsword: return 0.27;
                case Kind.Knife: return 0.26;
                case Kind.Axe: return 0.58;
                case Kind.Battleaxe: return 0.74;
                case Kind.Mace: return 0.81;
                case Kind.Sledge: return 0.74;
                case Kind.Spear: return 0.66;
                case Kind.Atgeir: return 0.40; // from just in front of the front hand, like the Thunderblood Atgeir
                case Kind.Staff: return 0.55;
                case Kind.Crossbow: return 0.69;
                default: return 0;
            }
        }

        /// <summary>
        /// Where the game's own variant weapons of a kind start their effects (the Frostfire ones): the same as
        /// <see cref="EffectStart"/> except atgeirs, whose Frostfire flames start at 55 %.
        /// </summary>
        public static double GameEffectStart(Kind kind) => kind == Kind.Atgeir ? 0.55 : EffectStart(kind);
    }
}
