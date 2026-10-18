using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeepNorthStaffVisuals
{
    /// <summary>
    /// Putting looks on weapon models. A staff's look is a note on it (<see cref="Looks.FromNote"/>). Players: the local
    /// player's game puts the look of their staff on show on their shared record (one int, <see cref="Looks.Code"/>), and
    /// every game with the mod reads every player's record, ours included, to dress the models in hand and on the back.
    /// Armor stands: each hand / back model wears the note of the item in its slot. Both every frame, after the game has
    /// built the models; a model is dressed only when its look changes (<see cref="Wear"/>).
    /// </summary>
    [HarmonyPatch]
    internal static class Dresser
    {
        /// <summary>The key the look is kept under on a player's record.</summary>
        private static readonly int s_lookKey = "DeepNorthStaffVisuals_look".GetStableHashCode();

        // A character's four weapon models and the item prefab hashes they were built from: right / left hand, right / left back.
        private static AccessTools.FieldRef<VisEquipment, GameObject>[] s_models;
        private static AccessTools.FieldRef<VisEquipment, int>[] s_hashes;
        private static AccessTools.FieldRef<VisEquipment, ZNetView> s_nview;

        // The items that can be on show: right hand, left hand, sheathed right, sheathed left.
        private static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData>[] s_items;

        /// <summary>The armor stand slot of each model, in <see cref="s_models"/> order.</summary>
        private static readonly VisSlot[] s_modelSlots = { VisSlot.HandRight, VisSlot.HandLeft, VisSlot.BackRight, VisSlot.BackLeft };

        /// <summary>The look last put on each model. Weak keys: a destroyed model isn't kept alive.</summary>
        private static ConditionalWeakTable<GameObject, Worn> s_worn = new ConditionalWeakTable<GameObject, Worn>();

        /// <summary>Each armor stand's looks per model, re-read when the stand's record changes.</summary>
        private static ConditionalWeakTable<VisEquipment, StandLooks> s_stands = new ConditionalWeakTable<VisEquipment, StandLooks>();

        /// <summary>The per-frame check failed outside of dressing (a game update?): it stays off until the next world.</summary>
        private static bool s_updateOff;

        private sealed class Worn
        {
            public string Look; // null until the first dressing: a model the game has just built always gets dressed
        }

        private sealed class StandLooks
        {
            public ArmorStand Stand;
            public bool Read;
            public uint Revision;
            public readonly string[] ByModel = new string[4];
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
            s_items = new[]
            {
                AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem"),
                AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem"),
                AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem"),
                AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem"),
            };
            return true;
        }

        /// <summary>Entering a world (or the main menu): no model has worn anything yet, and the per-frame check is back on.</summary>
        internal static void Reset()
        {
            s_worn = new ConditionalWeakTable<GameObject, Worn>();
            s_stands = new ConditionalWeakTable<VisEquipment, StandLooks>();
            s_updateOff = false;
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.CustomUpdate))]
        [HarmonyPostfix]
        private static void CustomUpdate(VisEquipment __instance)
        {
            if (s_updateOff) return;
            try
            {
                CheckLook(__instance);
            }
            catch (Exception e)
            {
                // Failing again every frame for every character would cost every frame; looks just stop changing instead.
                s_updateOff = true;
                Plugin.ErrorOnce("update", $"Looks stop changing until the next world (a game update?): {e}");
            }
        }

        /// <summary>
        /// Every frame, after the game has updated a character's equipment. Players: the local player's staff on show goes
        /// onto their record, and every player's models wear the look on their record. Armor stands: each model wears the
        /// look noted on its slot's item. Kept out of the patch method: if a game update removes something used here, the
        /// call fails inside the patch's try instead of in the game's update loop, which updates every character's
        /// equipment one after another.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CheckLook(VisEquipment vis)
        {
            if (!vis.m_isPlayer || ObjectDB.instance == null) return; // NPCs
            var nview = s_nview(vis);
            var record = nview != null ? nview.GetZDO() : null; // null for the main menu's preview (not networked)
            if (record == null) return;
            if (vis.m_isArmorStand)
            {
                CheckArmorStand(vis, record);
                return;
            }
            if (IsLocalPlayer(vis))
            {
                // Set sends the record only when the value changes, but Plain's 0 on a record without the key would add it.
                var mine = Looks.Code(Looks.FromNote(StaffOnShow()?.m_customData));
                if (record.GetInt(s_lookKey) != mine) record.Set(s_lookKey, mine, okForNotOwner: false);
            }
            // Ragdolls and the player-like NPCs pass too, but nobody writes a look onto their records: Plain.
            var look = Looks.FromCode(record.GetInt(s_lookKey));
            for (var i = 0; i < s_models.Length; i++) WearModel(vis, i, look);
        }

        /// <summary>
        /// An armor stand (its pose reads the stand's own record): each hand / back model wears the note of the item in the
        /// slot shown there. Empty slots (item "&lt;slot&gt;_item" 0 on the record, ArmorStand.cs:364) are skipped: their old
        /// item data can stay on the record. When two slots share a model the later one wins, as in the game.
        /// </summary>
        private static void CheckArmorStand(VisEquipment vis, ZDO record)
        {
            var looks = s_stands.GetValue(vis, v => new StandLooks { Stand = v.GetComponentInParent<ArmorStand>() });
            if (looks.Stand == null) return;
            if (!looks.Read || looks.Revision != record.DataRevision)
            {
                for (var i = 0; i < looks.ByModel.Length; i++) looks.ByModel[i] = Looks.Plain;
                var slots = looks.Stand.m_slots;
                for (var slot = 0; slot < slots.Count; slot++)
                {
                    var model = Array.IndexOf(s_modelSlots, slots[slot].m_slot);
                    if (model >= 0 && record.GetInt((slot + "_item").GetStableHashCode()) != 0)
                        looks.ByModel[model] = Looks.FromNote(NotesOf(record, slot));
                }
                looks.Read = true;
                looks.Revision = record.DataRevision;
            }
            for (var i = 0; i < s_models.Length; i++) WearModel(vis, i, looks.ByModel[i]);
        }

        /// <summary>The notes of an item saved on a record: a stand's slot, or -1 for an item stand's one item.</summary>
        internal static Dictionary<string, string> NotesOf(ZDO record, int slot)
        {
            var item = new ItemDrop.ItemData();
            ItemDrop.LoadFromZDO(item, record, slot);
            return item.m_customData;
        }

        private static void WearModel(VisEquipment vis, int i, string look)
        {
            var model = s_models[i](vis);
            if (model != null) Wear(model, ObjectDB.instance.GetItemPrefab(s_hashes[i](vis)), look);
        }

        /// <summary>
        /// Puts <paramref name="look"/> on a model unless it already wears it; a model the game has just built has worn
        /// nothing yet, so it always gets dressed (Plain included). A failure leaves that model plain (logged once).
        /// </summary>
        internal static void Wear(GameObject model, GameObject prefab, string look)
        {
            var worn = s_worn.GetOrCreateValue(model);
            if (worn.Look == look) return;
            worn.Look = look;
            try
            {
                Dress(model.transform, prefab, look);
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

        /// <summary>
        /// The local player's staff on show: the first item that can take a look in the right hand, left hand, sheathed
        /// right or sheathed left; null if none.
        /// </summary>
        private static ItemDrop.ItemData StaffOnShow()
        {
            var player = Player.m_localPlayer;
            if (player == null) return null;
            foreach (var slot in s_items)
            {
                var item = slot(player);
                if (item != null && EffectKit.LookKind(item.m_dropPrefab) != Kind.None) return item;
            }
            return null;
        }

        /// <summary>
        /// The key was pressed: the staff on show moves to its next look, named in the middle of the screen. The per-frame
        /// check puts it on the record and the models.
        /// </summary>
        internal static void OnKey()
        {
            if (s_updateOff) return;
            var staff = StaffOnShow();
            if (staff == null) return;
            var look = Looks.StepNote(staff.m_customData, EffectKit.IsAvailable);
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, look, log: false);
        }

        /// <summary>
        /// The player has a weapon model that can take a look: in a hand, or (handsOnly false) also on the back. False while
        /// the per-frame check is off (looks can't change then), so the hint hides.
        /// </summary>
        internal static bool HasLookable(bool handsOnly)
        {
            if (s_updateOff) return false;
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
            foreach (var part in donor.Parts)
                Copier.Copy(part, donor.Model, container.transform, fit, surface, Looks.RateBoost(look), Looks.RingRate(look),
                    Looks.RingLife(look));
            container.SetActive(true);
        }
    }
}
