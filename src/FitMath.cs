using System;
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

        public static V3 WithAxis(V3 v, int axis, double value) =>
            new V3(axis == 0 ? value : v.X, axis == 1 ? value : v.Y, axis == 2 ? value : v.Z);

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
    /// A model's own frame, read from the box around its meshes: length (the longest side), width (the middle one),
    /// thickness (the shortest), each as a model axis with a direction. Index 0 = length, 1 = width, 2 = thickness.
    /// </summary>
    internal sealed class ModelFrame
    {
        private const double Tiny = 1e-6;

        public readonly Box Box;

        /// <summary>The model axis (0 x, 1 y, 2 z) of length, width, thickness.</summary>
        public readonly int[] Axis = new int[3];

        /// <summary>+1 / -1: which way along that model axis the frame axis points.</summary>
        public readonly int[] Sign = new int[3];

        /// <param name="lengthSign">Which way along its model axis the length points, towards the head or tip (+1 / -1).</param>
        public ModelFrame(Box box, int lengthSign)
        {
            Box = box;
            var order = Enumerable.Range(0, 3).OrderByDescending(box.Size).ThenBy(a => a).ToArray();
            Axis[0] = order[0];
            Axis[1] = order[1];
            Axis[2] = order[2];
            Sign[0] = lengthSign >= 0 ? 1 : -1;
            Sign[1] = 1;
            // Thickness = length × width, so the frame is a rotation of the model axes, never a mirror image.
            Sign[2] = Sign[0] * Sign[1] * (IsCyclic(order[0], order[1]) ? 1 : -1);
        }

        /// <summary>(a, b, then the third) in x→y→z→x order.</summary>
        private static bool IsCyclic(int a, int b) => (a + 1) % 3 == b;

        public double Size(int k) => Box.Size(Axis[k]);

        /// <summary>The model-space unit vector of frame axis k.</summary>
        public V3 Unit(int k) => V3.Axis(Axis[k], Sign[k]);

        /// <summary>Where p lies along frame axis k: 0 at the box side the axis starts from, 1 at the side it points to.</summary>
        public double Fraction(V3 p, int k)
        {
            var a = Axis[k];
            var size = Box.Size(a);
            if (size < Tiny) return 0.5;
            return Sign[k] > 0 ? (p[a] - Box.Min[a]) / size : (Box.Max[a] - p[a]) / size;
        }

        /// <summary>The model coordinate (on axis Axis[k]) at a fraction along frame axis k.</summary>
        public double Coordinate(int k, double fraction)
        {
            var a = Axis[k];
            var size = Box.Size(a);
            return Sign[k] > 0 ? Box.Min[a] + fraction * size : Box.Max[a] - fraction * size;
        }
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

        /// <summary>A donor-space point → the target-space point (business end onto business end, plus the fix's shift).</summary>
        public V3 Point(V3 p)
        {
            var result = new V3(0, 0, 0);
            for (var k = 0; k < 3; k++)
            {
                var f = Donor.Fraction(p, k);
                if (k == 0) f = MapLength(f);
                result = V3.WithAxis(result, Target.Axis[k], Target.Coordinate(k, f + m_shift[k]));
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
