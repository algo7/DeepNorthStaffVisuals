using WeaponAuras;
using ItemType = ItemDrop.ItemData.ItemType;
using SkillType = Skills.SkillType;

internal static partial class Tests
{
    private static void Test_Kinds_OneAndTwoHanded()
    {
        Eq(Kind.Sword, WeaponKinds.KindOf(ItemType.OneHandedWeapon, SkillType.Swords, "SwordIron"), "iron sword");
        Eq(Kind.Greatsword, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.Swords, "THSwordKrom"), "Krom");
        Eq(Kind.Knife, WeaponKinds.KindOf(ItemType.OneHandedWeapon, SkillType.Knives, "KnifeBlackMetal"), "knife");
        Eq(Kind.Axe, WeaponKinds.KindOf(ItemType.OneHandedWeapon, SkillType.Axes, "AxeIron"), "axe");
        Eq(Kind.Battleaxe, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.Axes, "Battleaxe"), "battleaxe");
        Eq(Kind.Mace, WeaponKinds.KindOf(ItemType.OneHandedWeapon, SkillType.Clubs, "MaceIron"), "mace");
        Eq(Kind.Sledge, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.Clubs, "SledgeIron"), "sledge");
        Eq(Kind.Spear, WeaponKinds.KindOf(ItemType.OneHandedWeapon, SkillType.Spears, "SpearCarapace"), "spear");
        Eq(Kind.Atgeir, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.Polearms, "AtgeirIron"), "atgeir");
    }

    private static void Test_Kinds_RangedAndStaffs()
    {
        Eq(Kind.Bow, WeaponKinds.KindOf(ItemType.Bow, SkillType.Bows, "BowFineWood"), "bow");
        Eq(Kind.Crossbow, WeaponKinds.KindOf(ItemType.Bow, SkillType.Crossbows, "CrossbowArbalest"), "crossbow (a Bow item)");
        Eq(Kind.Staff, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.ElementalMagic, "StaffThunderBlood"), "elemental staff");
        Eq(Kind.Staff, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.BloodMagic, "StaffShield"), "blood staff");
        Eq(Kind.Staff, WeaponKinds.KindOf(ItemType.TwoHandedWeaponLeft, SkillType.BloodMagic, "StaffSkeleton"), "left-hand staff");
    }

    private static void Test_Kinds_NotWeapons()
    {
        Eq(Kind.None, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.Pickaxes, "PickaxeIron"), "pickaxe");
        Eq(Kind.None, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.Unarmed, "FistGold"), "fist weapon");
        Eq(Kind.None, WeaponKinds.KindOf(ItemType.TwoHandedWeapon, SkillType.Fishing, "FishingRod"), "fishing rod");
        Eq(Kind.None, WeaponKinds.KindOf(ItemType.OneHandedWeapon, SkillType.None, "BombOoze"), "bomb");
        Eq(Kind.None, WeaponKinds.KindOf(ItemType.Shield, SkillType.Blocking, "ShieldIronTower"), "shield");
        Eq(Kind.None, WeaponKinds.KindOf(ItemType.Torch, SkillType.Clubs, "Torch"), "torch");
        Eq(Kind.None, WeaponKinds.KindOf(ItemType.Tool, SkillType.Swords, "Hammer"), "tool");
        foreach (var horn in new[] { "Tankard", "TankardAnniversary", "TankardOdin", "Tankard_dvergr" })
            Eq(Kind.None, WeaponKinds.KindOf(ItemType.OneHandedWeapon, SkillType.Swords, horn), horn);
    }
}
