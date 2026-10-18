using System.Collections.Generic;
using System.Linq;
using DeepNorthStaffVisuals;

internal static partial class Tests
{
    private static void Test_Looks_CycleInOrder()
    {
        var look = Looks.Plain;
        var seen = new System.Collections.Generic.List<string>();
        for (var i = 0; i < 8; i++)
        {
            look = Looks.Next(look, _ => true);
            seen.Add(look);
        }
        Eq("Frostfire,Thunderblood,Bleeding,Storming,Primal,Dyrnwyn,Frostner,Plain", string.Join(",", seen), "the cycle");
    }

    private static void Test_Looks_SkipsUnavailable()
    {
        Eq("Bleeding", Looks.Next("Frostfire", l => l != "Thunderblood"), "Thunderblood missing");
        Eq(Looks.Plain, Looks.Next("Primal", l => l == "Frostfire"), "nothing after Primal available → Plain");
        Eq(Looks.Plain, Looks.Next(Looks.Plain, _ => false), "nothing available → Plain");
        Eq("Frostfire", Looks.Next("NoSuchLook", _ => true), "unknown look → the first");
    }

    private static void Test_Looks_OwnKindFirst()
    {
        Eq("SwordGold_FrostFire", Looks.DonorsFor("Frostfire", Kind.Sword)[0], "Frostfire sword");
        Eq("THSwordGold_BloodLightning", Looks.DonorsFor("Thunderblood", Kind.Greatsword)[0], "Thunderblood greatsword");
        Eq("MaceEldnerBlood", Looks.DonorsFor("Bleeding", Kind.Mace)[0], "Bleeding mace");
        Eq("BowAshlandsStorm", Looks.DonorsFor("Storming", Kind.Bow)[0], "Storming bow");
        Eq("CrossbowRipperNature", Looks.DonorsFor("Primal", Kind.Crossbow)[0], "Primal crossbow");
    }

    private static void Test_Looks_Fallbacks()
    {
        Eq("SwordNiedhoggBlood", Looks.DonorsFor("Bleeding", Kind.Knife)[0], "Bleeding knife → sword");
        Eq("MaceEldnerLightning", Looks.DonorsFor("Storming", Kind.Axe)[0], "Storming axe → (no battleaxe) mace");
        Eq("MaceEldnerNature", Looks.DonorsFor("Primal", Kind.Battleaxe)[0], "Primal battleaxe → (no axe, sledge) mace");
        Eq("MaceEldnerBlood", Looks.DonorsFor("Bleeding", Kind.Sledge)[0], "Bleeding sledge → mace");
        Eq("SpearSplitner_Lightning", Looks.DonorsFor("Storming", Kind.Atgeir)[0], "Storming atgeir → spear");
        Eq("AtgeirGold_FrostFire", Looks.DonorsFor("Frostfire", Kind.Staff)[0], "Frostfire staff → atgeir");
        Eq("SpearSplitner_Nature", Looks.DonorsFor("Primal", Kind.Staff)[0], "Primal staff → (no atgeir) spear");
        Eq("SwordDyrnwyn", Looks.DonorsFor("Dyrnwyn", Kind.Bow)[0], "Dyrnwyn on anything → the sword");
        Eq("MaceSilver", Looks.DonorsFor("Frostner", Kind.Spear)[0], "Frostner on anything → the mace");
    }

    private static void Test_Looks_EveryKindHasADonorForEveryLook()
    {
        foreach (var look in Looks.Order)
            foreach (Kind kind in System.Enum.GetValues(typeof(Kind)))
                True(Looks.DonorsFor(look, kind).Count > 0, $"{look} on {kind}");
        Eq(0, Looks.DonorsFor(Looks.Plain, Kind.Sword).Count, "Plain has no donors");
        Eq(11, Looks.DonorsFor("Frostfire", Kind.Sword).Count, "Frostfire: all eleven gold variants, no duplicates");
        True(Looks.DonorsFor("Frostfire", Kind.Sword).Distinct().Count() == 11, "no duplicates");
    }

    private static void Test_Fixes_EmptyList()
    {
        True(Fixes.For("SwordIron", "Frostfire") == null, "no fix for the iron sword");
    }

    private static void Test_Looks_WhichEmittersFollowTheWeapon()
    {
        foreach (var name in new[] { "flames", "flames (1)", "fx_Torch_Basic", "fx_Torch_Basic (1)", "Burny vfx", "Clouds", "Spores",
                     "Spores (1)", "Spores_World", "soft cloud", "vfx_BloodHit 1", "blood drip", "Sparcs", "Splats" })
            True(Looks.FollowsWeapon(name), $"{name} comes off the weapon's surface");
        foreach (var name in new[] { "flare", "Lightning", "Lightning (3)", "Swirls", "Point light", "" })
            False(Looks.FollowsWeapon(name), $"{name} keeps its own shape");
    }

    private static void Test_Looks_BleedingIsBoosted()
    {
        Eq(2.5, Looks.RateBoost("Bleeding"), "Bleeding: 2.5x the swirls and drips (it has no light and little else)");
        foreach (var look in new[] { "Frostfire", "Thunderblood", "Storming", "Primal", "Dyrnwyn", "Frostner", Looks.Plain, "NoSuchLook" })
            Eq(1.0, Looks.RateBoost(look), $"{look}: the game's rate");
    }

    private static void Test_Looks_ThunderbloodFewerBolts()
    {
        Eq(0.6, Looks.RingRate("Thunderblood"), "Thunderblood: 60 % of the game's bolts per ring");
        foreach (var look in new[] { "Frostfire", "Bleeding", "Storming", "Primal", "Dyrnwyn", "Frostner", Looks.Plain, "NoSuchLook" })
            Eq(1.0, Looks.RingRate(look), $"{look}: the game's rate per ring");
    }

    private static void Test_Looks_ThunderboltsFadeSooner()
    {
        Eq(0.6, Looks.RingLife("Thunderblood"), "Thunderblood's bolts live 60 % as long, so they don't fan out at the head");
        foreach (var look in new[] { "Frostfire", "Bleeding", "Storming", "Primal", "Dyrnwyn", "Frostner", Looks.Plain, "NoSuchLook" })
            Eq(1.0, Looks.RingLife(look), $"{look}: the game's lifetime");
        // the main bolts: fired at 0.5 m/s, pulled back at 0.05, living 2-3 s: 1.125 m, at 60 % 0.675 m
        var drift = Fit.Drift(true, 0.5, 0, -0.05, 2.5 * Looks.RingLife("Thunderblood"));
        Eq(1, Fit.RingFractions(1.13, Fit.Drift(true, 0.5, 0, -0.05, 2.5)).Count, "Echo Spike before: one spot, at the bottom");
        Eq(2, Fit.RingFractions(1.13, drift).Count, "Echo Spike: two spots");
        Eq(5, Fit.RingFractions(2.33, drift).Count, "Lightning Strike: five instead of four");
    }

    private static void Test_Looks_SwirlsSpreadAlongTheWeapon()
    {
        True(Looks.SpreadsAlongWeapon("Swirls"), "Bleeding's swirl rings repeat along the weapon");
        True(Looks.SpreadsAlongWeapon("Swirls (1)"), "copies count");
        True(Looks.SpreadsAlongWeapon("Lightning"), "Storming / Thunderblood bolts repeat along the weapon");
        True(Looks.SpreadsAlongWeapon("Lightning (4)"), "copies count");
        foreach (var name in new[] { "flames", "blood drip", "Clouds", "Sparcs", "Splats", "" })
            False(Looks.SpreadsAlongWeapon(name), name);
    }

    private static void Test_Looks_CodesNeverChange()
    {
        // Friends' games decode these: a changed code shows as Plain on their side. Values = the game's stable hash of the name.
        Eq(0, Looks.Code(Looks.Plain), "Plain");
        Eq(-77004796, Looks.Code("Frostfire"), "Frostfire");
        Eq(-1172106468, Looks.Code("Thunderblood"), "Thunderblood");
        Eq(924734674, Looks.Code("Bleeding"), "Bleeding");
        Eq(-951138885, Looks.Code("Storming"), "Storming");
        Eq(1473679531, Looks.Code("Primal"), "Primal");
        Eq(-1832295007, Looks.Code("Dyrnwyn"), "Dyrnwyn");
        Eq(-1360893387, Looks.Code("Frostner"), "Frostner");
        Eq(0, Looks.Code("NoSuchLook"), "unknown look");
        Eq(0, Looks.Code(null), "no look");
    }

    private static void Test_Looks_CodesReadBack()
    {
        foreach (var look in Looks.Order) Eq(look, Looks.FromCode(Looks.Code(look)), $"{look} round trip");
        Eq(Looks.Plain, Looks.FromCode(0), "0 → Plain");
        Eq(Looks.Plain, Looks.FromCode(12345), "a code this version doesn't know → Plain");
        Eq(Looks.Order.Length, Looks.Order.Select(Looks.Code).Distinct().Count(), "every look has its own code");
        False(Looks.Order.Any(l => Looks.Code(l) == 0), "no look shares Plain's 0");
    }

    private static void Test_Looks_NoteKeyNeverChanges()
    {
        // Every saved staff's look is under this key: changing it makes every staff Plain again.
        Eq("DeepNorthStaffVisuals_look", Looks.NoteKey, "note key");
    }

    private static void Test_Looks_NoteReadsTheLook()
    {
        Eq("Storming", Looks.FromNote(new Dictionary<string, string> { ["DeepNorthStaffVisuals_look"] = "Storming" }), "a look");
        Eq(Looks.Plain, Looks.FromNote(new Dictionary<string, string>()), "no note");
        Eq(Looks.Plain, Looks.FromNote(null), "no notes at all");
        Eq(Looks.Plain, Looks.FromNote(new Dictionary<string, string> { ["DeepNorthStaffVisuals_look"] = "Glitter" }), "a look this version doesn't know");
        Eq(Looks.Plain, Looks.FromNote(new Dictionary<string, string> { ["DeepNorthStaffVisuals_look"] = "Plain" }), "Plain written out");
    }

    private static void Test_Looks_NoteWritesTheLook()
    {
        var notes = new Dictionary<string, string> { ["SomeOtherMod"] = "x" };
        Looks.SetNote(notes, "Frostfire");
        Eq("Frostfire", notes["DeepNorthStaffVisuals_look"], "set");
        Looks.SetNote(notes, "Primal");
        Eq("Primal", notes["DeepNorthStaffVisuals_look"], "changed");
        Looks.SetNote(notes, Looks.Plain);
        False(notes.ContainsKey("DeepNorthStaffVisuals_look"), "Plain removes the note");
        Looks.SetNote(notes, "Glitter");
        False(notes.ContainsKey("DeepNorthStaffVisuals_look"), "an unknown look writes nothing");
        Eq("x", notes["SomeOtherMod"], "other notes untouched");
    }

    private static void Test_Looks_KeyStepsTheNote()
    {
        var notes = new Dictionary<string, string>();
        Eq("Frostfire", Looks.StepNote(notes, _ => true), "Plain → Frostfire");
        Eq("Frostfire", notes["DeepNorthStaffVisuals_look"], "written");
        Eq("Bleeding", Looks.StepNote(notes, l => l != "Thunderblood"), "an unavailable look is skipped");
        notes["DeepNorthStaffVisuals_look"] = "Glitter";
        Eq("Frostfire", Looks.StepNote(notes, _ => true), "a look this version doesn't know → the first");
        notes["DeepNorthStaffVisuals_look"] = "Frostner";
        Eq(Looks.Plain, Looks.StepNote(notes, _ => true), "after the last → Plain");
        False(notes.ContainsKey("DeepNorthStaffVisuals_look"), "Plain leaves no note");
    }

    private static void Test_Looks_TooltipLine()
    {
        Eq("\nLook: <color=orange>Frostfire</color>", Looks.TooltipLine("Frostfire"), "a look");
        Eq("", Looks.TooltipLine(Looks.Plain), "Plain");
        Eq("", Looks.TooltipLine("Glitter"), "unknown");
    }
}
