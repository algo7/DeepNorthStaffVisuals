using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeaponAuras
{
    /// <summary>
    /// What the game's weapons are made of, read from their prefabs and cached until the next world: which weapons can
    /// take a look, the donors' effect parts, and the box around a model. The prefabs themselves are never changed.
    /// </summary>
    internal static class EffectKit
    {
        /// <summary>Our container under a dressed model; the only thing the mod adds.</summary>
        public const string Container = "WeaponAuras_look";

        /// <summary>Children of a model that aren't the weapon itself: the upgrade glow, the swing trail, our container.</summary>
        private static readonly HashSet<string> s_skip = new HashSet<string> { "UpgraderGlow", "equiped", Container };

        internal sealed class Donor
        {
            public string Name;

            /// <summary>The donor's own kind: where its business end starts (WeaponKinds.GameEffectStart).</summary>
            public Kind Kind;

            /// <summary>The donor prefab's held model (its "attach" child): the parts' poses are read relative to it.</summary>
            public Transform Model;

            public ModelFrame Frame;

            /// <summary>The topmost objects under the model holding the effect's particle systems and lights.</summary>
            public List<Transform> Parts;
        }

        private static readonly Dictionary<string, Kind> s_kinds = new Dictionary<string, Kind>();
        private static readonly Dictionary<string, Donor> s_donors = new Dictionary<string, Donor>();
        private static readonly Dictionary<string, bool> s_available = new Dictionary<string, bool>();

        /// <summary>A new world: forget everything read (ObjectDB is rebuilt).</summary>
        public static void Clear()
        {
            s_kinds.Clear();
            s_donors.Clear();
            s_available.Clear();
        }

        /// <summary>The kind of an item prefab if it can take a look, else <see cref="Kind.None"/>.</summary>
        public static Kind LookKind(GameObject prefab)
        {
            if (prefab == null) return Kind.None;
            if (s_kinds.TryGetValue(prefab.name, out var cached)) return cached;
            var kind = ReadLookKind(prefab);
            s_kinds[prefab.name] = kind;
            return kind;
        }

        private static Kind ReadLookKind(GameObject prefab)
        {
            var item = prefab.GetComponent<ItemDrop>();
            if (item == null) return Kind.None;
            var shared = item.m_itemData.m_shared;
            var kind = WeaponKinds.KindOf(shared.m_itemType, shared.m_skillType, prefab.name);
            if (kind == Kind.None) return Kind.None;
            var held = HeldModel(prefab.transform);
            if (held == null || held.name != "attach") return Kind.None; // fitted to the body (fist weapons, Berserkir axes)
            if (HasOwnEffects(held)) return Kind.None;
            return ModelFrameOf(held, kind, out _, out _) ? kind : Kind.None;
        }

        /// <summary>The model the game puts in the hand: the first child named attach or attach_skin (VisEquipment.AttachItem).</summary>
        private static Transform HeldModel(Transform prefab)
        {
            foreach (Transform child in prefab)
                if (child.name == "attach" || child.name == "attach_skin") return child;
            return null;
        }

        /// <summary>A particle system or light anywhere in the model, outside the upgrade glow and the swing trail.</summary>
        private static bool HasOwnEffects(Transform t)
        {
            if (t.GetComponent<ParticleSystem>() != null || t.GetComponent<Light>() != null) return true;
            foreach (Transform child in t)
                if (!s_skip.Contains(child.name) && HasOwnEffects(child)) return true;
            return false;
        }

        /// <summary>
        /// A model's frame (see <see cref="ModelFrame"/>): its largest mesh's own axes, turned so the length points to the
        /// kind's head, and the extent of every mesh box along them, in the model's own space; main = that largest mesh.
        /// False if it has no mesh.
        /// </summary>
        public static bool ModelFrameOf(Transform model, Kind kind, out ModelFrame frame, out MeshFilter main)
        {
            frame = null;
            main = null;
            var corners = new List<V3>();
            V3[] axes = null;
            double[] sizes = null;
            var largest = -1.0;
            var toModel = model.worldToLocalMatrix;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || IsSkipped(filter.transform, model)) continue;
                var m = toModel * filter.transform.localToWorldMatrix;
                var b = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    corners.Add(new V3(p.x, p.y, p.z));
                }
                var meshAxes = new V3[3];
                var meshSizes = new double[3];
                for (var a = 0; a < 3; a++)
                {
                    Vector3 column = m.GetColumn(a);
                    meshAxes[a] = new V3(column.x, column.y, column.z);
                    meshSizes[a] = 2 * b.extents[a] * column.magnitude;
                }
                var size = Math.Max(meshSizes[0], Math.Max(meshSizes[1], meshSizes[2]));
                if (size > largest)
                {
                    largest = size;
                    axes = meshAxes;
                    sizes = meshSizes;
                    main = filter;
                }
            }
            if (axes == null) return false;
            frame = new ModelFrame(axes, sizes, WeaponKinds.HeadSign(kind), corners);
            return true;
        }

        /// <summary>Inside the upgrade glow, the swing trail or our container (between t and the model).</summary>
        private static bool IsSkipped(Transform t, Transform model)
        {
            for (; t != null && t != model; t = t.parent)
                if (s_skip.Contains(t.name)) return true;
            return false;
        }

        /// <summary>The first donor of this look for this kind that can be found, or null.</summary>
        public static Donor DonorFor(string look, Kind kind)
        {
            foreach (var name in Looks.DonorsFor(look, kind))
            {
                var donor = GetDonor(name);
                if (donor != null) return donor;
            }
            return null;
        }

        /// <summary>A look can be chosen when at least one of its donors can be found.</summary>
        public static bool IsAvailable(string look)
        {
            if (s_available.TryGetValue(look, out var available)) return available;
            available = DonorFor(look, Kind.None) != null;
            if (!available) Plugin.WarnOnce("look:" + look, $"The look {look} has no donor weapon in this game version: skipped");
            s_available[look] = available;
            return available;
        }

        private static Donor GetDonor(string name)
        {
            if (s_donors.TryGetValue(name, out var donor)) return donor;
            donor = ReadDonor(name);
            if (donor == null) Plugin.WarnOnce("donor:" + name, $"Donor weapon {name} or its effects weren't found (a game update?): skipped");
            s_donors[name] = donor;
            return donor;
        }

        private static Donor ReadDonor(string name)
        {
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
            var item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            var model = prefab != null ? prefab.transform.Find("attach") : null;
            if (item == null || model == null) return null;
            var shared = item.m_itemData.m_shared;
            var kind = WeaponKinds.KindOf(shared.m_itemType, shared.m_skillType, name);
            if (!ModelFrameOf(model, kind, out var frame, out _)) return null;
            var parts = new List<Transform>();
            FindParts(model, parts);
            if (parts.Count == 0) return null;
            return new Donor { Name = name, Kind = kind, Model = model, Frame = frame, Parts = parts };
        }

        /// <summary>The topmost children that hold particle systems or lights but no mesh of the weapon.</summary>
        private static void FindParts(Transform t, List<Transform> parts)
        {
            foreach (Transform child in t)
            {
                if (s_skip.Contains(child.name)) continue;
                if (child.GetComponentsInChildren<ParticleSystem>(true).Length == 0 && child.GetComponentsInChildren<Light>(true).Length == 0)
                    continue;
                if (child.GetComponentsInChildren<MeshFilter>(true).Length == 0) parts.Add(child);
                else FindParts(child, parts);
            }
        }
    }
}
