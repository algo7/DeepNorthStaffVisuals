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
        Near(new V3(0, 0, 1), frame.Unit(0), "length = +z");
        Near(new V3(1, 0, 0), frame.Unit(1), "width = +x");
        Near(new V3(0, 1, 0), frame.Unit(2), "thickness = z × x = +y");
        Near(0, frame.Fraction(new V3(0, 0, -0.2), 0), "hand end");
        Near(1, frame.Fraction(new V3(0, 0, 1.2), 0), "tip");
    }

    private static void Test_Fit_LengthSignIsGiven()
    {
        var frame = new ModelFrame(Splitnir, -1);
        Near(new V3(0, 0, -1), frame.Unit(0), "a spear's head points to -z");
        Near(new V3(0, -1, 0), frame.Unit(2), "(-z) × x = -y: still a rotation");
        Near(1, frame.Fraction(new V3(0, 0, -2.47), 0), "the head at -z is fraction 1");
        Near(new V3(0, 0, 1), new ModelFrame(Splitnir, 1).Unit(0), "the given direction wins over which end is farther");
    }

    private static void Test_Fit_EqualSidesPickTheLowerAxis()
    {
        var frame = new ModelFrame(IronMace, 1);
        Near(new V3(1, 0, 0), frame.Unit(1), "width = x when x and y are equal");
        Near(new V3(0, 1, 0), frame.Unit(2), "thickness = y");
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
        var fit = new Fit(atgeir, atgeir, 0.55, 0.40, null);
        Near(0.40, Lands(fit, 0.55), "the game's Frostfire Atgeir flames start at 55 %, ours at 40 %");
        Near(0.40 + 0.42 * 0.6 / 0.45, Lands(fit, 0.97), "and still end near the tip");
        Near(0.6 / 0.45, fit.EmitterStretch(new V3(0, 0, 1)), "the emitters grow by 60 / 45");
    }

    private static void Test_Fit_SameStartSameKindIsUnchanged()
    {
        var sword = new ModelFrame(GoldSword, 1);
        var fit = new Fit(sword, sword, 0.21, 0.21, null);
        foreach (var f in new[] { -0.1, 0.0, 0.1, 0.21, 0.5, 1.0 })
            Near(f, Lands(fit, f), $"fraction {f} stays");
        Near(1, fit.EmitterStretch(new V3(0, 0, 1)), "no stretch");
    }

    /// <summary>
    /// A model whose mesh is turned inside the model, like every atgeir (about 21° from z): the mesh's own axes are
    /// x' = (cos a, 0, -sin a), y' = y, z' = (sin a, 0, cos a); its box in those axes runs from min to max.
    /// </summary>
    private static ModelFrame Tilted(double degrees, V3 min, V3 max)
    {
        var a = degrees * Math.PI / 180;
        var axes = new[] { new V3(Math.Cos(a), 0, -Math.Sin(a)), new V3(0, 1, 0), new V3(Math.Sin(a), 0, Math.Cos(a)) };
        var corners = new System.Collections.Generic.List<V3>();
        for (var i = 0; i < 8; i++)
        {
            var x = (i & 1) == 0 ? min.X : max.X;
            var y = (i & 2) == 0 ? min.Y : max.Y;
            var z = (i & 4) == 0 ? min.Z : max.Z;
            corners.Add(x * axes[0] + y * axes[1] + z * axes[2]);
        }
        return new ModelFrame(axes, new[] { max.X - min.X, max.Y - min.Y, max.Z - min.Z }, 1, corners);
    }

    private static void Test_Fit_TiltedModelUsesTheMeshAxes()
    {
        var frame = Tilted(21, new V3(-0.15, -0.035, -0.75), new V3(0.15, 0.035, 2.14));
        var a = 21 * Math.PI / 180;
        Near(new V3(Math.Sin(a), 0, Math.Cos(a)), frame.Unit(0), "length runs along the tilted shaft");
        Near(2.89, frame.Size(0), "the shaft's real length");
        Near(0.30, frame.Size(1), "the head's real width, not the tilted box's");
    }

    private static void Test_Fit_StaysOnATiltedShaft()
    {
        var gold = Tilted(19, new V3(-0.22, -0.045, -0.49), new V3(0.22, 0.045, 2.07));
        var blackMetal = Tilted(21, new V3(-0.15, -0.035, -0.75), new V3(0.15, 0.035, 2.14));
        var fit = new Fit(gold, blackMetal, 0.55, 0.40, null);
        // a point in the middle of the gold shaft, 60 % along it
        var p = gold.Coordinate(0, 0.60) * gold.Unit(0) + gold.Coordinate(1, 0.5) * gold.Unit(1) + gold.Coordinate(2, 0.5) * gold.Unit(2);
        var q = fit.Point(p);
        Near(0.5, blackMetal.Fraction(q, 1), "still in the middle of the width");
        Near(0.5, blackMetal.Fraction(q, 2), "still in the middle of the thickness");
        Near(0.40 + 0.05 * 0.6 / 0.45, blackMetal.Fraction(q, 0), "business end onto business end along the shaft");
    }

    private static void Test_Fit_TracingRateFollowsTheWholeWeapon()
    {
        var dyrnwyn = new Fit(new ModelFrame(Dyrnwyn, 1), new ModelFrame(Atgeir, 1), 0.21, 0.40, null);
        Near(Fit.MaxRate, dyrnwyn.TraceRateFactor, "a whole 2.8 m atgeir from a 1.35 m blade: at most twice the rate");
        var sword = new ModelFrame(GoldSword, 1);
        Near(1.0 / 0.79, new Fit(sword, sword, 0.21, 0.21, null).TraceRateFactor, "the whole sword, not just its blade");
        var dagger = new ModelFrame(new Box(new V3(-0.07, -0.02, -0.1), new V3(0.07, 0.02, 0.6)), 1);
        Near(0.7 / (0.79 * 1.4), new Fit(sword, dagger, 0.21, 0.26, null).TraceRateFactor, "a dagger: fewer");
    }

    private static void Test_Fit_RingsAlongTheWeapon()
    {
        var staff = Fit.RingFractions(2.33);
        Eq(6, staff.Count, "Lightning Strike (2.33 m): a ring about every 40 cm");
        Near(0.08, staff[0], "from just above the bottom");
        Near(0.95, staff[staff.Count - 1], "to just below the top");
        for (var i = 1; i < staff.Count; i++) Near(0.87 / 5, staff[i] - staff[i - 1], "evenly spaced");
        Eq(3, Fit.RingFractions(1.13).Count, "Echo Spike (1.13 m): three");
        Eq(1, Fit.RingFractions(0.2).Count, "a tiny model: one ring");
        Near(0.515, Fit.RingFractions(0.2)[0], "in the middle");
    }

    private static void Test_Fit_RingsStopSoTheDriftEndsAtTheTip()
    {
        var staff = Fit.RingFractions(2.33, 0.87);
        Near(1 - 0.87 / 2.33, staff[staff.Count - 1], "the last ring's swirls drift up to the top, not past it");
        Eq(4, staff.Count, "Lightning Strike: four rings from 8 % to 63 %");
        var echo = Fit.RingFractions(1.13, 0.87);
        Eq(1, echo.Count, "Echo Spike: one ring");
        Near((0.08 + (1 - 0.87 / 1.13)) / 2, echo[0], "low on the staff");
        Eq(1, Fit.RingFractions(0.5, 0.87).Count, "drift longer than the model: one ring");
        Near(0.08, Fit.RingFractions(0.5, 0.87)[0], "at the bottom");
        Eq(Fit.RingFractions(2.33).Count, Fit.RingFractions(2.33, 0).Count, "no drift: up to 95 % as before");
    }

    private static void Test_Fit_DriftAlongTheAxis()
    {
        Near(0.67 * 1.3, Fit.Drift(false, 0, 0.67, 1.3), "swirls: their velocity along the axis");
        Near(1.0 * 1.75, Fit.Drift(true, 1.0, 0, 1.75), "Storming's bolts: fired from a cone along the axis");
        Near((0.5 + 0.2) * 2.5, Fit.Drift(true, 0.5, -0.2, 2.5), "both add up (as distances)");
        Near(0, Fit.Drift(false, 10, 0, 0.3), "splats burst sideways from a hemisphere: no drift along the axis");
    }
}
