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
    }
}
