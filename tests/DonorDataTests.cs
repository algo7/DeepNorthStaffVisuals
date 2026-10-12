using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DeepNorthStaffVisuals;

internal static partial class Tests
{
    /// <summary>
    /// Every donor weapon named in Looks exists in this game version: the game's asset list
    /// (StreamingAssets/SoftRef/manifest_extended, next to the Managed DLLs) holds its prefab. Skipped (and said so) when
    /// the list isn't there.
    /// </summary>
    private static void Test_Data_DonorsExist()
    {
        var managed = typeof(Tests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "ManagedDir").Value;
        var manifest = Path.Combine(managed, "..", "StreamingAssets", "SoftRef", "manifest_extended");
        if (!File.Exists(manifest))
        {
            System.Console.WriteLine($"SKIP Data_DonorsExist: no asset list at {manifest}");
            return;
        }
        var text = File.ReadAllText(manifest);
        foreach (var look in Looks.Order)
            foreach (Kind kind in Enum.GetValues(typeof(Kind)))
                foreach (var donor in Looks.DonorsFor(look, kind))
                    True(text.Contains("/" + donor + ".prefab"), $"donor {donor} ({look}) is in the game's assets");
    }
}
