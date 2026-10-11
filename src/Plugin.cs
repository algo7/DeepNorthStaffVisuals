using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace WeaponAuras
{
    [BepInPlugin(Guid, Name, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "algo7.weaponauras";
        public const string Name = "WeaponAuras";
        public const string PluginVersion = PluginInfo.Version; // from the git tag, generated at build time (MinVer)

        internal static ManualLogSource Log;

        private static readonly HashSet<string> s_logged = new HashSet<string>();

        private void Awake()
        {
            Log = Logger;
            if (Application.isBatchMode)
            {
                Log.LogInfo($"{Name} loaded (v{PluginVersion}): dedicated server, nothing to do");
                return;
            }
            Log.LogInfo($"{Name} loaded (v{PluginVersion})");
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
