using System.Collections.Generic;
using UnityEngine;

namespace WeaponAuras
{
    /// <summary>
    /// Copies one donor effect part under a container and fits it: every object moves to the same place relative to the new
    /// weapon (Fit.Point) and turns with it (Fit.Rotate); transform scales stay as the donor has them (some systems grow their
    /// particles with their parents' scale), only the emitter shapes stretch. Emitters that follow the weapon
    /// (Looks.FollowsWeapon) come off the weapon's surface instead, when it has one to give; ring emitters
    /// (Looks.SpreadsAlongWeapon) repeat along its length.
    /// </summary>
    internal static class Copier
    {
        /// <param name="surface">The weapon's main mesh to emit from, or null to keep every emitter's fitted shape.</param>
        /// <param name="rateBoost">Extra particle rate for the look (Looks.RateBoost).</param>
        public static void Copy(Transform part, Transform donorModel, Transform container, Fit fit, MeshRenderer surface, double rateBoost)
        {
            var copy = Object.Instantiate(part.gameObject, container, false).transform;
            var toDonor = donorModel.worldToLocalMatrix;
            var fromDonorRotation = Quaternion.Inverse(donorModel.rotation);
            var turn = Quaternion.LookRotation(ToUnity(fit.Rotate(new V3(0, 0, 1))), ToUnity(fit.Rotate(new V3(0, 1, 0))));

            // The part's own scale relative to the donor model (its parents' scales included), as in the donor.
            var relative = toDonor * part.localToWorldMatrix;
            copy.localScale = new Vector3(relative.GetColumn(0).magnitude, relative.GetColumn(1).magnitude, relative.GetColumn(2).magnitude);

            // Same hierarchy, same order (parents before children), so setting each pose top-down is final.
            var originals = part.GetComponentsInChildren<Transform>(true);
            var copies = copy.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < originals.Length && i < copies.Length; i++)
            {
                var p = toDonor.MultiplyPoint3x4(originals[i].position);
                copies[i].position = container.TransformPoint(ToUnity(fit.Point(new V3(p.x, p.y, p.z))));
                copies[i].rotation = container.rotation * turn * (fromDonorRotation * originals[i].rotation);
            }

            var originalSystems = part.GetComponentsInChildren<ParticleSystem>(true);
            var copiedSystems = copy.GetComponentsInChildren<ParticleSystem>(true);
            var rings = new List<ParticleSystem>();
            for (var i = 0; i < originalSystems.Length && i < copiedSystems.Length; i++)
            {
                var name = originalSystems[i].gameObject.name;
                if (surface != null && Looks.FollowsWeapon(name))
                    Trace(copiedSystems[i], surface, fit);
                else
                    Stretch(copiedSystems[i], fromDonorRotation * originalSystems[i].transform.rotation, fit);
                var emission = copiedSystems[i].emission;
                if (Looks.SpreadsAlongWeapon(name))
                {
                    // each ring at the game's own rate: more rings are already more of it
                    emission.rateOverTimeMultiplier = originalSystems[i].emission.rateOverTimeMultiplier;
                    rings.Add(copiedSystems[i]);
                }
                else
                {
                    emission.rateOverTimeMultiplier *= (float)rateBoost;
                }
            }
            // After the pairing loops: the clones change the copy's hierarchy.
            foreach (var ring in rings) Spread(ring, container, fit);
        }

        /// <summary>
        /// Repeats a ring emitter along the weapon's length (Fit.RingFractions), each copy turned like the fitted one.
        /// </summary>
        private static void Spread(ParticleSystem ring, Transform container, Fit fit)
        {
            var t = ring.transform;
            var axis = ToUnity(fit.Target.Unit(0));
            var fitted = container.InverseTransformPoint(t.position);
            var length = fit.Target.Size(0);
            var at = fit.Target.Fraction(FromUnity(fitted), 0);
            var rotation = t.rotation;
            var fractions = Fit.RingFractions(length, Drift(ring));
            for (var i = 0; i < fractions.Count; i++)
            {
                var copy = i == 0 ? t : Object.Instantiate(t.gameObject, t.parent, false).transform;
                copy.position = container.TransformPoint(fitted + axis * (float)((fractions[i] - at) * length));
                copy.rotation = rotation;
            }
        }

        /// <summary>
        /// How far a ring's particles travel along its own axis (the weapon's length) on average before they fade
        /// (Fit.Drift). Bleeding's swirls: 0.67 m/s for 1-1.6 s; Storming's bolts: fired from a cone at 1 m/s for 1.5-2 s.
        /// </summary>
        private static double Drift(ParticleSystem ring)
        {
            var velocity = ring.velocityOverLifetime;
            var shape = ring.shape.shapeType;
            var cone = shape == ParticleSystemShapeType.Cone || shape == ParticleSystemShapeType.ConeVolume;
            return Fit.Drift(cone, Mean(ring.main.startSpeed), velocity.enabled ? Mean(velocity.z) : 0, Mean(ring.main.startLifetime));
        }

        private static float Mean(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant: return curve.constant;
                case ParticleSystemCurveMode.TwoConstants: return (curve.constantMin + curve.constantMax) / 2;
                default: return curve.curveMultiplier;
            }
        }

        /// <summary>
        /// The emitter comes off the edges of the weapon's mesh (as the game's upgrade glow does), in the look's own colours;
        /// the rate follows the whole weapon's length.
        /// </summary>
        private static void Trace(ParticleSystem system, MeshRenderer surface, Fit fit)
        {
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.MeshRenderer;
            shape.meshRenderer = surface;
            shape.meshShapeType = ParticleSystemMeshShapeType.Edge;
            shape.useMeshColors = false;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;
            var emission = system.emission;
            emission.rateOverTimeMultiplier *= (float)fit.TraceRateFactor;
            // Spawned straight in the world, where the mesh is: a system simulating in its own space would convert the
            // positions through its parents' scale, which differs on the back joints.
            var main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }

        /// <summary>Stretches an emitter's shape (and its offset) along its own axes; rate follows the length.</summary>
        private static void Stretch(ParticleSystem system, Quaternion donorRotation, Fit fit)
        {
            var shape = system.shape;
            var shapeRotation = donorRotation * Quaternion.Euler(shape.rotation);
            shape.scale = Vector3.Scale(shape.scale, Along(shapeRotation, fit));
            shape.position = Vector3.Scale(shape.position, Along(donorRotation, fit));
            var emission = system.emission;
            emission.rateOverTimeMultiplier *= (float)fit.RateFactor;
        }

        /// <summary>The stretch along the x, y, z axes of something turned by rotation (in donor model space).</summary>
        private static Vector3 Along(Quaternion rotation, Fit fit) => new Vector3(
            (float)fit.EmitterStretch(FromUnity(rotation * Vector3.right)),
            (float)fit.EmitterStretch(FromUnity(rotation * Vector3.up)),
            (float)fit.EmitterStretch(FromUnity(rotation * Vector3.forward)));

        private static Vector3 ToUnity(V3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        private static V3 FromUnity(Vector3 v) => new V3(v.x, v.y, v.z);
    }
}
