using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DeepNorthStaffVisuals
{
    /// <summary>
    /// A staff on an item stand wears the look noted on the stand's item: when the stand builds its model, and on the
    /// game's own 4 s refresh, which catches a friend's game that built the model before the item's data arrived.
    /// </summary>
    [HarmonyPatch]
    internal static class StandPatches
    {
        private static AccessTools.FieldRef<ItemStand, GameObject> s_model;
        private static AccessTools.FieldRef<ItemStand, int> s_modelItem;
        private static AccessTools.FieldRef<ItemStand, ZNetView> s_nview;

        /// <summary>Each stand's look, re-read when its record changes. Weak keys: a destroyed stand isn't kept alive.</summary>
        private static ConditionalWeakTable<ItemStand, StandLook> s_looks = new ConditionalWeakTable<ItemStand, StandLook>();

        private sealed class StandLook
        {
            public bool Read;
            public uint Revision;
            public string Look;
        }

        private static bool Prepare()
        {
            s_model = AccessTools.FieldRefAccess<ItemStand, GameObject>("m_visualItem");
            s_modelItem = AccessTools.FieldRefAccess<ItemStand, int>("m_visualHash");
            s_nview = AccessTools.FieldRefAccess<ItemStand, ZNetView>("m_nview");
            return true;
        }

        internal static void Reset() => s_looks = new ConditionalWeakTable<ItemStand, StandLook>();

        [HarmonyPatch(typeof(ItemStand), "SetVisualItem")]
        [HarmonyPostfix]
        private static void SetVisualItem(ItemStand __instance) => Check(__instance);

        [HarmonyPatch(typeof(ItemStand), "UpdateVisual")]
        [HarmonyPostfix]
        private static void UpdateVisual(ItemStand __instance) => Check(__instance);

        private static void Check(ItemStand stand)
        {
            try
            {
                Wear(stand);
            }
            catch (Exception e)
            {
                Plugin.ErrorOnce("stand", $"Couldn't show the look on an item stand (the staff stays plain): {e}");
            }
        }

        /// <summary>Outside the patch methods, so a game update that removes something used here fails inside the try.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Wear(ItemStand stand)
        {
            var model = s_model(stand);
            var nview = s_nview(stand);
            var record = nview != null ? nview.GetZDO() : null;
            if (model == null || record == null || ObjectDB.instance == null) return;
            // The model's own item, not the record's: a friend's game can build a new model (a sword hung after the staff)
            // before the record catches up, and the record's old staff would put its look on the sword for good.
            var prefab = ObjectDB.instance.GetItemPrefab(s_modelItem(stand));
            if (EffectKit.LookKind(prefab) == Kind.None) return;
            var look = s_looks.GetOrCreateValue(stand);
            if (!look.Read || look.Revision != record.DataRevision)
            {
                look.Look = Looks.FromNote(Dresser.NotesOf(record, -1));
                look.Read = true;
                look.Revision = record.DataRevision;
            }
            Dresser.Wear(model, prefab, look.Look);
        }
    }

    /// <summary>A dropped staff wears the look noted on it, once, when it appears: its data is final by then.</summary>
    [HarmonyPatch]
    internal static class GroundPatches
    {
        [HarmonyPatch(typeof(ItemDrop), "Start")]
        [HarmonyPostfix]
        private static void Start(ItemDrop __instance)
        {
            try
            {
                Wear(__instance);
            }
            catch (Exception e)
            {
                Plugin.ErrorOnce("ground", $"Couldn't show the look on a dropped staff (it stays plain): {e}");
            }
        }

        /// <summary>Outside the patch method, so a game update that removes something used here fails inside the try.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Wear(ItemDrop drop)
        {
            var item = drop.m_itemData;
            if (item == null || EffectKit.LookKind(item.m_dropPrefab) == Kind.None) return;
            var look = Looks.FromNote(item.m_customData);
            if (look == Looks.Plain) return;
            var model = drop.transform.Find("attach"); // the model on the ground, as in hand
            if (model != null) Dresser.Wear(model.gameObject, item.m_dropPrefab, look);
        }
    }

    /// <summary>
    /// The tooltip's last line names a staff's look ("Look: Frostfire"); nothing for Plain, and nothing on the crafting
    /// screen (an upgraded staff comes out Plain).
    /// </summary>
    [HarmonyPatch]
    internal static class TooltipPatches
    {
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        private static void GetTooltip(ItemDrop.ItemData item, bool crafting, ref string __result)
        {
            if (crafting) return;
            try
            {
                __result += LookLine(item);
            }
            catch (Exception e)
            {
                Plugin.ErrorOnce("tooltip", $"Couldn't add the look to a tooltip: {e}");
            }
        }

        /// <summary>Outside the patch method, so a game update that removes something used here fails inside the try.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string LookLine(ItemDrop.ItemData item) =>
            item != null && EffectKit.LookKind(item.m_dropPrefab) != Kind.None ? Looks.TooltipLine(Looks.FromNote(item.m_customData)) : "";
    }
}
