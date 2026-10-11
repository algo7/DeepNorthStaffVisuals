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

        /// <param name="lengthFromFarEnd">
        /// True: length points to the box end farther from the hand (the model origin), i.e. the tip. False (bows: the hand
        /// is mid-bow, so neither end is the tip): length points along its model axis.
        /// </param>
        public ModelFrame(Box box, bool lengthFromFarEnd)
        {
            Box = box;
            var order = Enumerable.Range(0, 3).OrderByDescending(box.Size).ThenBy(a => a).ToArray();
            Axis[0] = order[0];
            Axis[1] = order[1];
            Axis[2] = order[2];
            Sign[0] = !lengthFromFarEnd || Math.Abs(box.Max[order[0]]) >= Math.Abs(box.Min[order[0]]) ? 1 : -1;
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
    /// Maps a donor weapon's effect onto another weapon: points keep their fractions along length / width / thickness,
    /// directions turn with the frames, emitters stretch by the size ratios (within limits). Unity-free.
    /// </summary>
    internal sealed class Fit
    {
        public const double MinLengthStretch = 0.25, MaxLengthStretch = 4;
        public const double MinSideStretch = 0.5, MaxSideStretch = 2;
        public const double MinRate = 0.5, MaxRate = 2;

        public readonly ModelFrame Donor, Target;
        private readonly double[] m_stretch = new double[3];
        private readonly double[] m_shift = new double[3];

        /// <summary>How much the donor's particle rate is multiplied: the length ratio within limits.</summary>
        public readonly double RateFactor;

        public Fit(ModelFrame donor, ModelFrame target, Fix fix)
        {
            Donor = donor;
            Target = target;
            var extra = fix?.Stretch ?? 1;
            for (var k = 0; k < 3; k++)
            {
                var ratio = Ratio(k);
                var stretch = k == 0 ? Clamp(ratio, MinLengthStretch, MaxLengthStretch) : Clamp(ratio, MinSideStretch, MaxSideStretch);
                m_stretch[k] = stretch * extra;
            }
            RateFactor = Clamp(Ratio(0), MinRate, MaxRate);
            m_shift[0] = fix?.Along ?? 0;
            m_shift[1] = fix?.Across ?? 0;
        }

        private double Ratio(int k)
        {
            var donorSize = Donor.Size(k);
            return donorSize < 1e-6 ? 1 : Target.Size(k) / donorSize;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;

        /// <summary>A donor-space point → the target-space point at the same fractions (plus the fix's shift).</summary>
        public V3 Point(V3 p)
        {
            var result = new V3(0, 0, 0);
            for (var k = 0; k < 3; k++)
                result = V3.WithAxis(result, Target.Axis[k], Target.Coordinate(k, Donor.Fraction(p, k) + m_shift[k]));
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
