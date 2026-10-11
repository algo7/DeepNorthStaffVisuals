using UnityEngine;

namespace WeaponAuras
{
    /// <summary>
    /// Copies one donor effect part under a container and fits it: every object moves to the same place relative to the new
    /// weapon (Fit.Point) and turns with it (Fit.Rotate); transform scales stay as the donor has them (some systems grow their
    /// particles with their parents' scale), only the emitter shapes stretch.
    /// </summary>
    internal static class Copier
    {
        public static void Copy(Transform part, Transform donorModel, Transform container, Fit fit)
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
            for (var i = 0; i < originalSystems.Length && i < copiedSystems.Length; i++)
                Stretch(copiedSystems[i], fromDonorRotation * originalSystems[i].transform.rotation, fit);
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
