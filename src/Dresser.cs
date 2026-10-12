using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeaponAuras
{
    /// <summary>
    /// Players' looks and putting them on their weapon models. A player's look lives on their shared record (one int, see
    /// <see cref="Looks.Code"/>): the local player's game writes it, and every game with the mod reads it for every
    /// player, ours included, to dress the models in hand and on the back (VisEquipment.AttachItem for a new model, a
    /// per-frame check for a changed look).
    /// </summary>
    [HarmonyPatch]
    internal static class Dresser
    {
        /// <summary>The local player's current look: Plain on every world entry, never saved.</summary>
        internal static string Current = Looks.Plain;

        /// <summary>The key the look is kept under on a player's record.</summary>
        private static readonly int s_lookKey = "WeaponAuras_look".GetStableHashCode();

        // The player's four weapon models and the item prefab hashes they were built from: right / left hand, right / left back.
        private static AccessTools.FieldRef<VisEquipment, GameObject>[] s_models;
        private static AccessTools.FieldRef<VisEquipment, int>[] s_hashes;
        private static AccessTools.FieldRef<VisEquipment, ZNetView> s_nview;

        /// <summary>The look code last put on each player's models. Weak keys: a destroyed player isn't kept alive.</summary>
        private static ConditionalWeakTable<VisEquipment, Applied> s_applied = new ConditionalWeakTable<VisEquipment, Applied>();

        private sealed class Applied
        {
            public int Code; // 0 (Plain) at first: a player's models start plain
        }

        /// <summary>Harmony calls this before patching: a missing field fails the patch (logged by Plugin.TryPatch).</summary>
        private static bool Prepare()
        {
            s_models = new[]
            {
                AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance"),
                AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance"),
                AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightBackItemInstance"),
                AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftBackItemInstance"),
            };
            s_hashes = new[]
            {
                AccessTools.FieldRefAccess<VisEquipment, int>("m_currentRightItemHash"),
                AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLeftItemHash"),
                AccessTools.FieldRefAccess<VisEquipment, int>("m_currentRightBackItemHash"),
                AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLeftBackItemHash"),
            };
            s_nview = AccessTools.FieldRefAccess<VisEquipment, ZNetView>("m_nview");
            return true;
        }

        /// <summary>Entering a world (or the main menu): Plain, and no player's look applied yet.</summary>
        internal static void Reset()
        {
            Current = Looks.Plain;
            s_applied = new ConditionalWeakTable<VisEquipment, Applied>();
        }

        [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
        [HarmonyPostfix]
        private static void AttachItem(VisEquipment __instance, GameObject __result, int itemHash)
        {
            if (__result == null) return;
            try
            {
                var record = RecordOf(__instance);
                if (record == null || ObjectDB.instance == null) return;
                var look = Looks.FromCode(record.GetInt(s_lookKey));
                if (look != Looks.Plain) Dress(__result.transform, ObjectDB.instance.GetItemPrefab(itemHash), look);
            }
            catch (Exception e)
            {
                Plugin.ErrorOnce("dress", $"Couldn't put the look on a weapon (it stays plain): {e}");
            }
        }

        /// <summary>
        /// Every frame, after the game has updated a character's equipment: the local player's look goes onto their record,
        /// and a player whose record shows another look than their models wear gets all four models re-dressed.
        /// </summary>
        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.CustomUpdate))]
        [HarmonyPostfix]
        private static void CustomUpdate(VisEquipment __instance)
        {
            try
            {
                var record = RecordOf(__instance);
                if (record == null || ObjectDB.instance == null) return;
                if (IsLocalPlayer(__instance))
                {
                    // Set sends the record only when the value changes, but Plain's 0 on a record without the key would add it.
                    var mine = Looks.Code(Current);
                    if (record.GetInt(s_lookKey) != mine) record.Set(s_lookKey, mine, okForNotOwner: false);
                }
                var code = record.GetInt(s_lookKey);
                var applied = s_applied.GetOrCreateValue(__instance);
                if (applied.Code == code) return;
                applied.Code = code;
                var look = Looks.FromCode(code);
                for (var i = 0; i < s_models.Length; i++)
                {
                    var model = s_models[i](__instance);
                    if (model != null) Dress(model.transform, ObjectDB.instance.GetItemPrefab(s_hashes[i](__instance)), look);
                }
            }
            catch (Exception e)
            {
                Plugin.ErrorOnce("update", $"Couldn't update a player's look (their staff keeps the one it has): {e}");
            }
        }

        /// <summary>
        /// A player's shared record; null for NPCs, armor stands and the main menu's character preview (not networked).
        /// Ragdolls and the player-like NPCs have one, but nobody writes a look onto it, so they read as Plain.
        /// </summary>
        private static ZDO RecordOf(VisEquipment vis)
        {
            if (!vis.m_isPlayer || vis.m_isArmorStand) return null;
            var nview = s_nview(vis);
            return nview != null ? nview.GetZDO() : null;
        }

        private static bool IsLocalPlayer(VisEquipment vis)
        {
            var player = Player.m_localPlayer;
            return player != null && vis.gameObject == player.gameObject;
        }

        /// <summary>
        /// The key was pressed: next look and its name in the middle of the screen. The per-frame check puts it on the
        /// record and the models.
        /// </summary>
        internal static void OnKey()
        {
            if (!HasLookable(handsOnly: false)) return;
            Current = Looks.Next(Current, EffectKit.IsAvailable);
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Current, log: false);
        }

        /// <summary>The player has a weapon model that can take a look: in a hand, or (handsOnly false) also on the back.</summary>
        internal static bool HasLookable(bool handsOnly)
        {
            var vis = LocalVisEquipment();
            if (vis == null || ObjectDB.instance == null) return false;
            for (var i = 0; i < (handsOnly ? 2 : s_models.Length); i++)
                if (s_models[i](vis) != null && EffectKit.LookKind(ObjectDB.instance.GetItemPrefab(s_hashes[i](vis))) != Kind.None)
                    return true;
            return false;
        }

        private static VisEquipment LocalVisEquipment()
        {
            var player = Player.m_localPlayer;
            return player != null ? player.GetComponent<VisEquipment>() : null;
        }

        /// <summary>Takes the previous look off a model, then puts <paramref name="look"/> on if the weapon can take it.</summary>
        private static void Dress(Transform model, GameObject prefab, string look)
        {
            foreach (Transform child in model)
                if (child.name == EffectKit.Container) Object.Destroy(child.gameObject);
            if (look == Looks.Plain) return;
            var kind = EffectKit.LookKind(prefab);
            if (kind == Kind.None) return;
            var fix = Fixes.For(prefab.name, look);
            var donor = EffectKit.DonorFor(look, fix != null && fix.DonorKind != Kind.None ? fix.DonorKind : kind);
            if (donor == null || !EffectKit.ModelFrameOf(model, kind, out var frame, out var main)) return;
            var fit = new Fit(donor.Frame, frame, WeaponKinds.GameEffectStart(donor.Kind), WeaponKinds.EffectStart(kind), fix);
            // Flames, clouds, spores and frost come off the weapon's own surface, like the game's upgrade glow does; a mesh
            // whose data isn't kept in memory leaves them in their fitted shape.
            var surface = main.GetComponent<MeshRenderer>();
            if (surface == null || !EffectKit.CanEmitFrom(model, main)) surface = null;
            // The copies wake up only once they're fitted: LightFlicker keeps the position it wakes up at and puts the
            // light back there every frame, and world-space particles would start at the unfitted spot.
            var container = new GameObject(EffectKit.Container);
            container.SetActive(false);
            container.transform.SetParent(model, false);
            foreach (var part in donor.Parts) Copier.Copy(part, donor.Model, container.transform, fit, surface, Looks.RateBoost(look));
            container.SetActive(true);
        }
    }
}
