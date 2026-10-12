using System;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeaponAuras
{
    /// <summary>
    /// The current look and putting it on the player's own weapon models: when the game builds a model in the hand or on
    /// the back (VisEquipment.AttachItem), and on the models already there when the key changes the look.
    /// </summary>
    [HarmonyPatch]
    internal static class Dresser
    {
        /// <summary>The current look: Plain on every world entry, never saved.</summary>
        internal static string Current = Looks.Plain;

        // The player's four weapon models and the item prefab hashes they were built from: right / left hand, right / left back.
        private static AccessTools.FieldRef<VisEquipment, GameObject>[] s_models;
        private static AccessTools.FieldRef<VisEquipment, int>[] s_hashes;

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
            return true;
        }

        [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
        [HarmonyPostfix]
        private static void AttachItem(VisEquipment __instance, GameObject __result, int itemHash)
        {
            if (__result == null || Current == Looks.Plain) return;
            try
            {
                if (!IsLocalPlayer(__instance) || ObjectDB.instance == null) return;
                Dress(__result.transform, ObjectDB.instance.GetItemPrefab(itemHash));
            }
            catch (Exception e)
            {
                Plugin.ErrorOnce("dress", $"Couldn't put the look on a weapon (it stays plain): {e}");
            }
        }

        private static bool IsLocalPlayer(VisEquipment vis)
        {
            var player = Player.m_localPlayer;
            return player != null && vis.gameObject == player.gameObject;
        }

        /// <summary>The key was pressed: next look, its name in the middle of the screen, every model re-dressed.</summary>
        internal static void OnKey()
        {
            if (!HasLookable(handsOnly: false)) return;
            Current = Looks.Next(Current, EffectKit.IsAvailable);
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Current, log: false);
            var vis = LocalVisEquipment();
            for (var i = 0; i < s_models.Length; i++)
            {
                var model = s_models[i](vis);
                if (model != null) Dress(model.transform, ObjectDB.instance.GetItemPrefab(s_hashes[i](vis)));
            }
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

        /// <summary>Takes the previous look off a model, then puts the current one on if the weapon can take it.</summary>
        private static void Dress(Transform model, GameObject prefab)
        {
            foreach (Transform child in model)
                if (child.name == EffectKit.Container) Object.Destroy(child.gameObject);
            if (Current == Looks.Plain) return;
            var kind = EffectKit.LookKind(prefab);
            if (kind == Kind.None) return;
            var fix = Fixes.For(prefab.name, Current);
            var donor = EffectKit.DonorFor(Current, fix != null && fix.DonorKind != Kind.None ? fix.DonorKind : kind);
            if (donor == null || !EffectKit.ModelBox(model, out var box)) return;
            var fit = new Fit(donor.Frame, new ModelFrame(box, WeaponKinds.HeadSign(kind)),
                WeaponKinds.GameEffectStart(donor.Kind), WeaponKinds.EffectStart(kind), fix);
            // The copies wake up only once they're fitted: LightFlicker keeps the position it wakes up at and puts the
            // light back there every frame, and world-space particles would start at the unfitted spot.
            var container = new GameObject(EffectKit.Container);
            container.SetActive(false);
            container.transform.SetParent(model, false);
            foreach (var part in donor.Parts) Copier.Copy(part, donor.Model, container.transform, fit);
            container.SetActive(true);
        }
    }
}
