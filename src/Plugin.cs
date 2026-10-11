using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeaponAuras
{
    [BepInPlugin(Guid, Name, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "algo7.weaponauras";
        public const string Name = "WeaponAuras";
        public const string PluginVersion = PluginInfo.Version; // from the git tag, generated at build time (MinVer)

        internal static ManualLogSource Log;

        private static ConfigEntry<KeyboardShortcut> s_key;
        private static Func<Player, bool> s_takeInput;
        private static bool s_running;
        private static readonly HashSet<string> s_logged = new HashSet<string>();

        /// <summary>The key as the settings show it, e.g. "L" or "LeftShift + L".</summary>
        internal static string KeyName => s_key.Value.ToString();

        private void Awake()
        {
            Log = Logger;
            if (Application.isBatchMode)
            {
                Log.LogInfo($"{Name} loaded (v{PluginVersion}): dedicated server, nothing to do");
                return;
            }
            s_key = Config.Bind("General", "Change look key", new KeyboardShortcut(KeyCode.L),
                "Steps through the looks while you have a weapon equipped that can take one.");
            try
            {
                // The game's own "is the player taking input" check (false with chat, inventory, map or a menu open).
                s_takeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));
            }
            catch (Exception e)
            {
                Log.LogError($"Looks are off: the game's input check wasn't found (a game update?): {e}");
                return;
            }
            var harmony = new Harmony(Guid);
            if (!TryPatch(harmony, typeof(Dresser), "equipment hook"))
            {
                Log.LogError("Looks are off: without the equipment hook nothing can be shown");
                return;
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            s_running = true;
            Log.LogInfo($"{Name} loaded (v{PluginVersion})");
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        /// <summary>Entering a world (or the main menu) starts on Plain: nothing is saved. ObjectDB is new, so are the caches.</summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Dresser.Current = Looks.Plain;
            EffectKit.Clear();
        }

        private void Update()
        {
            if (!s_running) return;
            try
            {
                var player = Player.m_localPlayer;
                if (player == null || !KeyPressed() || !s_takeInput(player)) return;
                Dresser.OnKey();
            }
            catch (Exception e)
            {
                ErrorOnce("key", $"Changing the look failed: {e}");
            }
        }

        /// <summary>The key went down this frame with its modifiers held, read through the game's input.</summary>
        private static bool KeyPressed()
        {
            var key = s_key.Value;
            if (key.MainKey == KeyCode.None || !ZInput.GetKeyDown(key.MainKey, false)) return false;
            foreach (var modifier in key.Modifiers)
                if (!ZInput.GetKey(modifier, false)) return false;
            return true;
        }

        /// <summary>True when patched; false (logged) on failure.</summary>
        private static bool TryPatch(Harmony harmony, Type patches, string what)
        {
            try
            {
                harmony.PatchAll(patches);
                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"Could not install the {what}: {e}");
                return false;
            }
        }

        /// <summary>Logs an error once per kind per session (a failing frame would otherwise flood the log).</summary>
        internal static void ErrorOnce(string kind, string message)
        {
            if (s_logged.Add("error:" + kind)) Log.LogError(message);
        }

        internal static void WarnOnce(string kind, string message)
        {
            if (s_logged.Add("warn:" + kind)) Log.LogWarning(message);
        }
    }
}
