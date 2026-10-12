using System;
using System.Collections.Generic;
using System.Linq;

namespace WeaponAuras
{
    /// <summary>A point or direction in a model's own space (x, y, z = axes 0, 1, 2). Unity-free.</summary>
    internal readonly struct V3
    {
        public readonly double X, Y, Z;

        public V3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static V3 operator *(double s, V3 v) => new V3(s * v.X, s * v.Y, s * v.Z);

        public static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>The unit vector of axis 0, 1 or 2, times sign.</summary>
        public static V3 Axis(int axis, int sign) => new V3(axis == 0 ? sign : 0, axis == 1 ? sign : 0, axis == 2 ? sign : 0);

        public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        /// <summary>This vector at length 1 (a zero vector stays zero).</summary>
        public V3 Normalized() => Length < 1e-12 ? this : (1 / Length) * this;

        /// <summary>The component with the largest magnitude, with its sign.</summary>
        public double Largest() => Math.Abs(X) >= Math.Abs(Y) && Math.Abs(X) >= Math.Abs(Z) ? X : Math.Abs(Y) >= Math.Abs(Z) ? Y : Z;

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>An axis-aligned box in a model's own space.</summary>
    internal readonly struct Box
    {
        public readonly V3 Min, Max;

        public Box(V3 min, V3 max)
        {
            Min = min;
            Max = max;
        }

        public double Size(int axis) => Max[axis] - Min[axis];
    }

    /// <summary>
    /// A model's own frame: length, width and thickness as unit vectors in model space, read from its largest mesh's own
    /// axes (the mesh can sit turned inside the model, like every atgeir's), and the model's extent along each, from the
    /// corners of all its mesh boxes. Index 0 = length, 1 = width, 2 = thickness.
    /// </summary>
    internal sealed class ModelFrame
    {
        private const double Tiny = 1e-6;

        /// <summary>Length (towards the head or tip), width, thickness: a rotation of the model's axes, never a mirror.</summary>
        public readonly V3[] Axes = new V3[3];

        /// <summary>The model's extent along each frame axis.</summary>
        public readonly double[] Min = new double[3], Max = new double[3];

        /// <param name="meshAxes">The largest mesh's x, y, z axes in model space.</param>
        /// <param name="meshSizes">That mesh's size along each of them.</param>
        /// <param name="headSign">
        /// Which way the head or tip points: length is turned so its z component (or, if it has almost none, its largest
        /// component) has this sign.
        /// </param>
        /// <param name="corners">The corners of every mesh box of the model, in model space.</param>
        public ModelFrame(V3[] meshAxes, double[] meshSizes, int headSign, IEnumerable<V3> corners)
        {
            var order = Enumerable.Range(0, 3).OrderByDescending(i => meshSizes[i]).ThenBy(i => i).ToArray();
            var length = meshAxes[order[0]].Normalized();
            var width = meshAxes[order[1]].Normalized();
            var reference = Math.Abs(length.Z) >= 0.1 ? length.Z : length.Largest();
            if (reference < 0 != headSign < 0) length = -1 * length;
            if (width.Largest() < 0) width = -1 * width;
            Axes[0] = length;
            Axes[1] = width;
            Axes[2] = V3.Cross(length, width);
            for (var k = 0; k < 3; k++)
            {
                Min[k] = double.MaxValue;
                Max[k] = double.MinValue;
            }
            foreach (var c in corners)
                for (var k = 0; k < 3; k++)
                {
                    var d = V3.Dot(c, Axes[k]);
                    if (d < Min[k]) Min[k] = d;
                    if (d > Max[k]) Max[k] = d;
                }
        }

        /// <summary>A model that is one axis-aligned box (its mesh not turned).</summary>
        public ModelFrame(Box box, int headSign)
            : this(new[] { V3.Axis(0, 1), V3.Axis(1, 1), V3.Axis(2, 1) }, new[] { box.Size(0), box.Size(1), box.Size(2) }, headSign,
                Enumerable.Range(0, 8).Select(i => new V3((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                    (i & 4) == 0 ? box.Min.Z : box.Max.Z)))
        {
        }

        public double Size(int k) => Max[k] - Min[k];

        /// <summary>The model-space unit vector of frame axis k.</summary>
        public V3 Unit(int k) => Axes[k];

        /// <summary>Where p lies along frame axis k: 0 at the side the axis starts from, 1 at the side it points to.</summary>
        public double Fraction(V3 p, int k)
        {
            var size = Size(k);
            return size < Tiny ? 0.5 : (V3.Dot(p, Axes[k]) - Min[k]) / size;
        }

        /// <summary>The position along frame axis k (as a distance along Unit(k)) at a fraction of the extent.</summary>
        public double Coordinate(int k, double fraction) => Min[k] + fraction * Size(k);
    }

    /// <summary>
    /// Maps a donor weapon's effect onto another weapon. Along the length, the donor's business end (from where its blade
    /// or head starts to the tip) lands on the target's, and the part before it on the part before; width and thickness
    /// keep their fractions. Directions turn with the frames; emitters stretch by the size ratios (within limits).
    /// Unity-free.
    /// </summary>
    internal sealed class Fit
    {
        public const double MinLengthStretch = 0.25, MaxLengthStretch = 4;
        public const double MinSideStretch = 0.5, MaxSideStretch = 2;
        public const double MinRate = 0.5, MaxRate = 2;

        public readonly ModelFrame Donor, Target;
        private readonly double[] m_stretch = new double[3];
        private readonly double[] m_shift = new double[3];
        private readonly double m_donorStart, m_targetStart;

        /// <summary>How much the donor's particle rate is multiplied: the business ends' length ratio within limits.</summary>
        public readonly double RateFactor;

        /// <summary>
        /// The rate factor for an emitter that comes off the whole weapon's surface: the target's whole length over the
        /// donor's business end (which its emitter covered), within limits.
        /// </summary>
        public readonly double TraceRateFactor;

        /// <param name="donorStart">Where the donor's business end starts (fraction of its length from the butt).</param>
        /// <param name="targetStart">Where the target's business end starts.</param>
        public Fit(ModelFrame donor, ModelFrame target, double donorStart, double targetStart, Fix fix)
        {
            Donor = donor;
            Target = target;
            m_donorStart = donorStart;
            m_targetStart = targetStart;
            var extra = fix?.Stretch ?? 1;
            var lengthRatio = BusinessEndRatio();
            m_stretch[0] = Clamp(lengthRatio, MinLengthStretch, MaxLengthStretch) * extra;
            for (var k = 1; k < 3; k++) m_stretch[k] = Clamp(Ratio(k), MinSideStretch, MaxSideStretch) * extra;
            RateFactor = Clamp(lengthRatio, MinRate, MaxRate);
            var donorEnd = (1 - donorStart) * donor.Size(0);
            TraceRateFactor = donorEnd < 1e-6 ? 1 : Clamp(target.Size(0) / donorEnd, MinRate, MaxRate);
            m_shift[0] = fix?.Along ?? 0;
            m_shift[1] = fix?.Across ?? 0;
        }

        private double Ratio(int k)
        {
            var donorSize = Donor.Size(k);
            return donorSize < 1e-6 ? 1 : Target.Size(k) / donorSize;
        }

        /// <summary>The target's business end length over the donor's.</summary>
        private double BusinessEndRatio()
        {
            var donorEnd = (1 - m_donorStart) * Donor.Size(0);
            return donorEnd < 1e-6 ? 1 : (1 - m_targetStart) * Target.Size(0) / donorEnd;
        }

        /// <summary>A donor length fraction → the target's: [0, donorStart] → [0, targetStart], the rest → [targetStart, 1].</summary>
        private double MapLength(double f)
        {
            if (f < m_donorStart && m_donorStart > 1e-6) return f * m_targetStart / m_donorStart;
            var donorEnd = 1 - m_donorStart;
            return donorEnd < 1e-6 ? f : m_targetStart + (f - m_donorStart) * (1 - m_targetStart) / donorEnd;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;

        /// <summary>
        /// Where along a weapon of this length (m) to put repeated rings: one about every <paramref name="spacing"/> m from
        /// <paramref name="from"/> to <paramref name="to"/> (fractions of the length), evenly spaced; at least one, in the
        /// middle.
        /// </summary>
        public static List<double> RingFractions(double length, double spacing = 0.4, double from = 0.08, double to = 0.95)
        {
            var count = Math.Max(1, (int)Math.Round((to - from) * length / spacing) + 1);
            var fractions = new List<double>();
            if (count == 1)
            {
                fractions.Add((from + to) / 2);
                return fractions;
            }
            for (var i = 0; i < count; i++) fractions.Add(from + i * (to - from) / (count - 1));
            return fractions;
        }

        /// <summary>A donor-space point → the target-space point (business end onto business end, plus the fix's shift).</summary>
        public V3 Point(V3 p)
        {
            var result = new V3(0, 0, 0);
            for (var k = 0; k < 3; k++)
            {
                var f = Donor.Fraction(p, k);
                if (k == 0) f = MapLength(f);
                result = result + Target.Coordinate(k, f + m_shift[k]) * Target.Unit(k);
            }
            return result;
        }

        /// <summary>A donor-space direction → the target-space direction (turned with the frames, same length).</summary>
        public V3 Rotate(V3 d)
        {
            var result = new V3(0, 0, 0);
            for (var k = 0; k < 3; k++) result = result + V3.Dot(d, Donor.Unit(k)) * Target.Unit(k);
            return result;
        }

        /// <summary>How much an emitter stretches along a donor-space direction (1 for a zero vector).</summary>
        public double EmitterStretch(V3 d)
        {
            var length = d.Length;
            if (length < 1e-9) return 1;
            var stretched = new V3(0, 0, 0);
            for (var k = 0; k < 3; k++) stretched = stretched + (V3.Dot(d, Donor.Unit(k)) * m_stretch[k]) * Target.Unit(k);
            return stretched.Length / length;
        }
    }
}
