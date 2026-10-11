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
        var frame = new ModelFrame(GoldSword, lengthFromFarEnd: true);
        Eq(2, frame.Axis[0], "length = z");
        Eq(0, frame.Axis[1], "width = x");
        Eq(1, frame.Axis[2], "thickness = y");
        Eq(1, frame.Sign[0], "length points to +z (the far end)");
        Eq(1, frame.Sign[2], "z × x = +y");
        Near(0, frame.Fraction(new V3(0, 0, -0.2), 0), "hand end");
        Near(1, frame.Fraction(new V3(0, 0, 1.2), 0), "tip");
    }

    private static void Test_Fit_LengthTowardsTheFarEnd()
    {
        var frame = new ModelFrame(Splitnir, lengthFromFarEnd: true);
        Eq(2, frame.Axis[0], "length = z");
        Eq(-1, frame.Sign[0], "length points to -z (the far end)");
        Eq(-1, frame.Sign[2], "(-z) × x = -y: still a rotation");
        Near(1, frame.Fraction(new V3(0, 0, -2.47), 0), "the tip at -z is fraction 1");
        var bow = new ModelFrame(Splitnir, lengthFromFarEnd: false);
        Eq(1, bow.Sign[0], "bows keep the model's own direction");
    }

    private static void Test_Fit_EqualSidesPickTheLowerAxis()
    {
        var frame = new ModelFrame(IronMace, lengthFromFarEnd: true);
        Eq(0, frame.Axis[1], "width = x when x and y are equal");
        Eq(1, frame.Axis[2], "thickness = y");
    }

    private static void Test_Fit_NeverAMirror()
    {
        var sizes = new[] { new V3(1, 2, 3), new V3(3, 2, 1), new V3(2, 3, 1), new V3(1, 3, 2), new V3(2, 1, 3), new V3(3, 1, 2) };
        foreach (var size in sizes)
            foreach (var far in new[] { true, false })
                foreach (var flip in new[] { 1.0, -1.0 })
                {
                    // flip puts the far end on the negative side of every axis
                    var box = flip > 0 ? new Box(new V3(-0.1, -0.1, -0.1), size) : new Box(-1 * size, new V3(0.1, 0.1, 0.1));
                    var f = new ModelFrame(box, far);
                    var l = f.Unit(0);
                    var w = f.Unit(1);
                    var t = f.Unit(2);
                    var cross = new V3(l.Y * w.Z - l.Z * w.Y, l.Z * w.X - l.X * w.Z, l.X * w.Y - l.Y * w.X);
                    Near(t, cross, $"length × width = thickness for {size} flip {flip} far {far}");
                }
    }

    private static void Test_Fit_PointsKeepTheirFractions()
    {
        var fit = new Fit(new ModelFrame(GoldSword, true), new ModelFrame(IronSword, true), null);
        Near(IronSword.Min, fit.Point(GoldSword.Min), "hand-end corner");
        Near(IronSword.Max, fit.Point(GoldSword.Max), "tip corner");
        // 70 % up the gold blade, centred → 70 % up the iron blade, centred
        Near(new V3(0, 0, -0.17 + 0.7 * 1.27), fit.Point(new V3(0, 0, -0.2 + 0.7 * 1.4)), "flames 70 % up");
    }

    private static void Test_Fit_OntoAReversedSpear()
    {
        var fit = new Fit(new ModelFrame(IronSword, true), new ModelFrame(Splitnir, true), null);
        Near(-2.47, fit.Point(new V3(0, 0, 1.1)).Z, "the sword's tip lands on Splitnir's tip (-z)");
        Near(0.5, fit.Point(new V3(0, 0, -0.17)).Z, "hand end on hand end");
        Near(new V3(0, 0, -1), fit.Rotate(new V3(0, 0, 1)), "up the blade turns to -z");
        Near(new V3(1, 0, 0), fit.Rotate(new V3(1, 0, 0)), "width stays x");
        Near(new V3(0, -1, 0), fit.Rotate(new V3(0, 1, 0)), "thickness turns with it");
    }

    private static void Test_Fit_StretchWithinLimits()
    {
        var sword = new ModelFrame(GoldSword, true);
        var same = new Fit(sword, sword, null);
        Near(1, same.EmitterStretch(new V3(0, 0, 1)), "same weapon: no stretch");
        Near(1, same.RateFactor, "same weapon: same rate");

        var tiny = new ModelFrame(new Box(new V3(-0.01, -0.001, -0.01), new V3(0.01, 0.001, 0.1)), true);
        var shrink = new Fit(sword, tiny, null);
        Near(Fit.MinLengthStretch, shrink.EmitterStretch(new V3(0, 0, 1)), "length shrinks at most to 0.25");
        Near(Fit.MinSideStretch, shrink.EmitterStretch(new V3(1, 0, 0)), "width shrinks at most to 0.5");
        Near(Fit.MinRate, shrink.RateFactor, "rate drops at most to half");

        var huge = new ModelFrame(new Box(new V3(-5, -1, -1), new V3(5, 1, 20)), true);
        var grow = new Fit(sword, huge, null);
        Near(Fit.MaxLengthStretch, grow.EmitterStretch(new V3(0, 0, 1)), "length grows at most 4×");
        Near(Fit.MaxSideStretch, grow.EmitterStretch(new V3(0, 1, 0)), "thickness grows at most 2×");
        Near(Fit.MaxRate, grow.RateFactor, "rate at most doubles");
        Near(1, grow.EmitterStretch(new V3(0, 0, 0)), "zero vector: 1");
    }

    private static void Test_Fit_ShortWeaponFewerParticles()
    {
        var fit = new Fit(new ModelFrame(GoldSword, true), new ModelFrame(new Box(new V3(-0.07, -0.02, -0.1), new V3(0.07, 0.02, 0.6)), true), null);
        Near(0.5, fit.RateFactor, "a 0.7 m dagger from a 1.4 m sword: half the rate");
        Near(0.5, fit.EmitterStretch(new V3(0, 0, 1)), "and half the emitter length");
    }

    private static void Test_Fit_FixShiftsAndStretches()
    {
        var fix = new Fix { Along = 0.1, Across = -0.5, Stretch = 2 };
        var sword = new ModelFrame(GoldSword, true);
        var fit = new Fit(sword, sword, fix);
        Near(-0.2 + 0.6 * 1.4, fit.Point(new V3(0, 0, -0.2 + 0.5 * 1.4)).Z, "a tenth further up");
        Near(-0.13, fit.Point(new V3(0, 0, 0)).X, "half the width across: to the edge");
        Near(2, fit.EmitterStretch(new V3(0, 0, 1)), "twice the stretch");
    }
}
