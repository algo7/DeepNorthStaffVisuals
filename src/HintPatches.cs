using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeepNorthStaffVisuals
{
    /// <summary>
    /// The key hint (bottom-right button list): a "Change look" row, cloned from the game's Block row, shown while combat
    /// hints are up and a weapon in hand can take a look. Keyboard only (there's no controller button). If the row can't be
    /// built, a warning is logged once and there's no hint for the session. Never throws.
    /// </summary>
    [HarmonyPatch]
    internal static class HintPatches
    {
        internal const string RowName = "DeepNorthStaffVisuals_hint";
        internal const string Label = "Change look";

        /// <summary>The hints the row belongs to: the HUD is new on every world load.</summary>
        private static KeyHints s_hints;
        private static GameObject s_row;
        private static bool s_off;

        [HarmonyPatch(typeof(KeyHints), "UpdateHints")]
        [HarmonyPostfix]
        private static void UpdateHints(KeyHints __instance)
        {
            if (s_off) return;
            try
            {
                Refresh(__instance);
            }
            catch (Exception e)
            {
                TurnOff($"The 'Change look' key hint failed; no hint from now on: {e}");
            }
        }

        /// <summary>
        /// Builds the row once per HUD and shows it with a weapon that can take a look out. Outside the patch method, so a
        /// game update that removes something used here fails inside the patch's try, not in the game's hint update.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Refresh(KeyHints hints)
        {
            if (hints != s_hints)
            {
                s_hints = hints;
                s_row = null;
            }
            var combat = hints.m_combatHints;
            var show = combat != null && combat.activeSelf && Dresser.HasLookable(handsOnly: true);
            if (s_row == null)
            {
                if (!show) return;
                s_row = BuildRow(combat.transform);
                if (s_row == null)
                {
                    TurnOff("The key hint's Block row wasn't found (a game update?): no 'Change look' hint");
                    return;
                }
            }
            if (s_row.activeSelf != show) s_row.SetActive(show);
        }

        /// <summary>A copy of CombatHints/Keyboard/Block (Text + key_bkg/Key) at the end of the keyboard list.</summary>
        private static GameObject BuildRow(Transform combat)
        {
            var block = combat.Find("Keyboard/Block");
            if (block == null) return null;
            var row = Object.Instantiate(block.gameObject, block.parent, false);
            row.name = RowName;
            var text = row.transform.Find("Text");
            var key = row.transform.Find("key_bkg/Key");
            var textLabel = text != null ? text.GetComponent<TMP_Text>() : null;
            var keyLabel = key != null ? key.GetComponent<TMP_Text>() : null;
            if (textLabel == null || keyLabel == null)
            {
                Object.Destroy(row);
                return null;
            }
            textLabel.text = Label;
            keyLabel.text = Plugin.KeyName;
            return row;
        }

        private static void TurnOff(string message)
        {
            s_off = true;
            Plugin.Log.LogWarning(message);
            if (s_row != null) s_row.SetActive(false);
        }
    }
}
