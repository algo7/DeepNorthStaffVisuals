using System;
using WeaponAuras;

internal static partial class Tests
{
    // Boxes as measured in game data (attach space): the Frostfire Sword, the Iron Sword, Splitnir (length along -z),
    // the Iron Mace (width = thickness), the Nord Bow (length along -z) and the Crude Bow (+z).
    private static readonly Box GoldSword = new Box(new V3(-0.13, -0.03, -0.2), new V3(0.13, 0.03, 1.2));
    private static readonly Box IronSword = new Box(new V3(-0.07, -0.025, -0.17), new V3(0.07, 0.025, 1.1));
    private static readonly Box Splitnir = new Box(new V3(-0.095, -0.035, -2.47), new V3(0.095, 0.035, 0.5));
    private static readonly Box IronMace = new Box(new V3(-0.11, -0.11, -0.15), new V3(0.11, 0.11, 0.88));

    private static void Near(double expected, double actual, string what) =>
        True(Math.Abs(expected - actual) < 1e-6, $"{what}: expected {expected}, got {actual}");

    private static void Near(V3 expected, V3 actual, string what)
    {
        Near(expected.X, actual.X, what + ".x");
        Near(expected.Y, actual.Y, what + ".y");
        Near(expected.Z, actual.Z, what + ".z");
    }

    private static void Test_Fit_FrameOfASword()
    {
        var frame = new ModelFrame(GoldSword, 1);
        Eq(2, frame.Axis[0], "length = z");
        Eq(0, frame.Axis[1], "width = x");
        Eq(1, frame.Axis[2], "thickness = y");
        Eq(1, frame.Sign[0], "length points to +z (the far end)");
        Eq(1, frame.Sign[2], "z × x = +y");
        Near(0, frame.Fraction(new V3(0, 0, -0.2), 0), "hand end");
        Near(1, frame.Fraction(new V3(0, 0, 1.2), 0), "tip");
    }

    private static void Test_Fit_LengthSignIsGiven()
    {
        var frame = new ModelFrame(Splitnir, -1);
        Eq(2, frame.Axis[0], "length = z");
        Eq(-1, frame.Sign[0], "a spear's head points to -z");
        Eq(-1, frame.Sign[2], "(-z) × x = -y: still a rotation");
        Near(1, frame.Fraction(new V3(0, 0, -2.47), 0), "the head at -z is fraction 1");
        Eq(1, new ModelFrame(Splitnir, 1).Sign[0], "the given direction wins over which end is farther");
    }

    private static void Test_Fit_EqualSidesPickTheLowerAxis()
    {
        var frame = new ModelFrame(IronMace, 1);
        Eq(0, frame.Axis[1], "width = x when x and y are equal");
        Eq(1, frame.Axis[2], "thickness = y");
    }

    private static void Test_Fit_NeverAMirror()
    {
        var sizes = new[] { new V3(1, 2, 3), new V3(3, 2, 1), new V3(2, 3, 1), new V3(1, 3, 2), new V3(2, 1, 3), new V3(3, 1, 2) };
        foreach (var size in sizes)
            foreach (var sign in new[] { 1, -1 })
                foreach (var flip in new[] { 1.0, -1.0 })
                {
                    // flip puts the far end on the negative side of every axis
                    var box = flip > 0 ? new Box(new V3(-0.1, -0.1, -0.1), size) : new Box(-1 * size, new V3(0.1, 0.1, 0.1));
                    var f = new ModelFrame(box, sign);
                    var l = f.Unit(0);
                    var w = f.Unit(1);
                    var t = f.Unit(2);
                    var cross = new V3(l.Y * w.Z - l.Z * w.Y, l.Z * w.X - l.X * w.Z, l.X * w.Y - l.Y * w.X);
                    Near(t, cross, $"length × width = thickness for {size} flip {flip} sign {sign}");
                }
    }

    private static void Test_Fit_PointsKeepTheirFractions()
    {
        var fit = new Fit(new ModelFrame(GoldSword, 1), new ModelFrame(IronSword, 1), 0, 0, null);
        Near(IronSword.Min, fit.Point(GoldSword.Min), "hand-end corner");
        Near(IronSword.Max, fit.Point(GoldSword.Max), "tip corner");
        // 70 % up the gold blade, centred → 70 % up the iron blade, centred
        Near(new V3(0, 0, -0.17 + 0.7 * 1.27), fit.Point(new V3(0, 0, -0.2 + 0.7 * 1.4)), "flames 70 % up");
    }

    private static void Test_Fit_OntoAReversedSpear()
    {
        var fit = new Fit(new ModelFrame(IronSword, 1), new ModelFrame(Splitnir, -1), 0, 0, null);
        Near(-2.47, fit.Point(new V3(0, 0, 1.1)).Z, "the sword's tip lands on Splitnir's tip (-z)");
        Near(0.5, fit.Point(new V3(0, 0, -0.17)).Z, "hand end on hand end");
        Near(new V3(0, 0, -1), fit.Rotate(new V3(0, 0, 1)), "up the blade turns to -z");
        Near(new V3(1, 0, 0), fit.Rotate(new V3(1, 0, 0)), "width stays x");
        Near(new V3(0, -1, 0), fit.Rotate(new V3(0, 1, 0)), "thickness turns with it");
    }

    private static void Test_Fit_StretchWithinLimits()
    {
        var sword = new ModelFrame(GoldSword, 1);
        var same = new Fit(sword, sword, 0, 0, null);
        Near(1, same.EmitterStretch(new V3(0, 0, 1)), "same weapon: no stretch");
        Near(1, same.RateFactor, "same weapon: same rate");

        var tiny = new ModelFrame(new Box(new V3(-0.01, -0.001, -0.01), new V3(0.01, 0.001, 0.1)), 1);
        var shrink = new Fit(sword, tiny, 0, 0, null);
        Near(Fit.MinLengthStretch, shrink.EmitterStretch(new V3(0, 0, 1)), "length shrinks at most to 0.25");
        Near(Fit.MinSideStretch, shrink.EmitterStretch(new V3(1, 0, 0)), "width shrinks at most to 0.5");
        Near(Fit.MinRate, shrink.RateFactor, "rate drops at most to half");

        var huge = new ModelFrame(new Box(new V3(-5, -1, -1), new V3(5, 1, 20)), 1);
        var grow = new Fit(sword, huge, 0, 0, null);
        Near(Fit.MaxLengthStretch, grow.EmitterStretch(new V3(0, 0, 1)), "length grows at most 4×");
        Near(Fit.MaxSideStretch, grow.EmitterStretch(new V3(0, 1, 0)), "thickness grows at most 2×");
        Near(Fit.MaxRate, grow.RateFactor, "rate at most doubles");
        Near(1, grow.EmitterStretch(new V3(0, 0, 0)), "zero vector: 1");
    }

    private static void Test_Fit_ShortWeaponFewerParticles()
    {
        var fit = new Fit(new ModelFrame(GoldSword, 1), new ModelFrame(new Box(new V3(-0.07, -0.02, -0.1), new V3(0.07, 0.02, 0.6)), 1), 0, 0, null);
        Near(0.5, fit.RateFactor, "a 0.7 m dagger from a 1.4 m sword: half the rate");
        Near(0.5, fit.EmitterStretch(new V3(0, 0, 1)), "and half the emitter length");
    }

    private static void Test_Fit_FixShiftsAndStretches()
    {
        var fix = new Fix { Along = 0.1, Across = -0.5, Stretch = 2 };
        var sword = new ModelFrame(GoldSword, 1);
        var fit = new Fit(sword, sword, 0, 0, fix);
        Near(-0.2 + 0.6 * 1.4, fit.Point(new V3(0, 0, -0.2 + 0.5 * 1.4)).Z, "a tenth further up");
        Near(-0.13, fit.Point(new V3(0, 0, 0)).X, "half the width across: to the edge");
        Near(2, fit.EmitterStretch(new V3(0, 0, 1)), "twice the stretch");
    }

    // Dyrnwyn (a 1.71 m sword) and the Black Metal Atgeir (2.80 m), boxes as measured; hands at 13 % and 27 %.
    private static readonly Box Dyrnwyn = new Box(new V3(-0.1, -0.02, -0.22), new V3(0.1, 0.02, 1.49));
    private static readonly Box Atgeir = new Box(new V3(-0.6, -0.2, -0.76), new V3(0.6, 0.25, 2.04));

    /// <summary>Where a point at donor length fraction f lands on the target, as a target length fraction.</summary>
    private static double Lands(Fit fit, double f) =>
        fit.Target.Fraction(fit.Point(new V3(0, 0, fit.Donor.Coordinate(0, f))), 0);

    private static void Test_Fit_BusinessEndOntoAnotherKind()
    {
        var fit = new Fit(new ModelFrame(Dyrnwyn, 1), new ModelFrame(Atgeir, 1), 0.21, 0.40, null);
        Near(0.40, Lands(fit, 0.21), "where the sword's blade starts → where the atgeir's business end starts");
        Near(1.0, Lands(fit, 1.0), "tip on tip");
        Near(0.70, Lands(fit, 0.605), "half-way up the blade → half-way up the business end");
        Near(0.20, Lands(fit, 0.105), "the handle half-way → half-way to the business end");
        Near(0.6 * 2.80 / (0.79 * 1.71), fit.EmitterStretch(new V3(0, 0, 1)), "emitters stretch by the business ends' lengths");
        Near(0.6 * 2.80 / (0.79 * 1.71), fit.RateFactor, "rate follows the business end's length");
    }

    private static void Test_Fit_LongerOnAtgeirs()
    {
        var atgeir = new ModelFrame(Atgeir, 1);
        var fit = new Fit(atgeir, atgeir, 0.53, 0.40, null);
        Near(0.40, Lands(fit, 0.53), "the game's Frostfire Atgeir flames start at 53 %, ours at 40 %");
        Near(0.40 + 0.41 * 0.6 / 0.47, Lands(fit, 0.94), "and still end near the tip");
        Near(0.6 / 0.47, fit.EmitterStretch(new V3(0, 0, 1)), "the emitters grow by 60 / 47");
    }

    private static void Test_Fit_SameStartSameKindIsUnchanged()
    {
        var sword = new ModelFrame(GoldSword, 1);
        var fit = new Fit(sword, sword, 0.21, 0.21, null);
        foreach (var f in new[] { -0.1, 0.0, 0.1, 0.21, 0.5, 1.0 })
            Near(f, Lands(fit, f), $"fraction {f} stays");
        Near(1, fit.EmitterStretch(new V3(0, 0, 1)), "no stretch");
    }
}
