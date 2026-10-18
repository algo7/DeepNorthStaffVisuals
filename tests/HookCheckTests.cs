using System;
using System.Linq;
using System.Reflection;

// The hook check (HookCheck.cs) against the hooks broken on purpose (FakeHooks.cs), then against the real plugin.
// Only HookCheck_PluginHooksMatchTheGame failing (and, in a repo with transpilers, its anchor tests) means the plugin is
// affected; the fakes hook fake classes, so a game update can't break them. The same file in every mod repo.
internal static partial class Tests
{
    private static string Metadata(string key) =>
        typeof(Tests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value
        ?? throw new Exception($"no {key} in the test assembly's metadata (AssemblyMetadata in the tests' .csproj)");

    private static string ManagedDir => Metadata("ManagedDir");
    private static string PluginDll => Metadata("PluginDll");

    private static HookCheck.Report FakeReport() =>
        HookCheck.Run(typeof(Tests).Assembly.Location, ManagedDir, t => t.Namespace == "FakeHooks");

    private static void Test_HookCheck_ReportsTheBrokenFakes()
    {
        var expected = new[]
        {
            "Broken.Missing: FakeHooks.FakeGame.NoSuchMethod not found",
            "Broken.Ambiguous: FakeHooks.FakeGame.Damage is overloaded",
            "Broken.WrongArguments: FakeHooks.FakeGame.Damage(System.String) not found",
            "Broken.Tiny: FakeHooks.FakeGame.Tiny is 1 bytes of IL",
            "Broken.FinalTiny: FakeHooks.FakeImpl.Bar is 1 bytes of IL",
            "Broken.Aggressive: FakeHooks.FakeAggressive.Big is marked AggressiveInlining",
            "Broken.BaseCall: FakeHooks.FakeVirtualBase.Qux is 1 bytes of IL",
            "Broken.Unsupported: [HarmonyPatch(System.Type, HarmonyLib.MethodType)] isn't read by the hook check",
            "Broken.Helper: has [HarmonyPatch] but Harmony won't run it",
            "Broken.Two: FakeHooks.FakeGame.NoSuchTarget not found",
            "Broken.Names: AccessTools.Field(typeof(FakeHooks.FakeGame), \"m_noSuchField\") not found",
            "Broken.Names: AccessTools.Method(typeof(FakeHooks.FakeGame), \"NoSuchMethod\") not found",
            "Broken.Names: AccessTools.Method(typeof(FakeHooks.FakeDerived), \"Foo\") is overloaded",
            "Broken.MoreNames: AccessTools.FieldRefAccess<FakeHooks.FakeGame, System.Int32>(\"m_noSuchField\") not found",
            "Broken.MoreNames: AccessTools.FieldRefAccess<FakeHooks.FakeGame, System.String>(\"m_own\"): the field is System.Int32",
            "Broken.MoreNames: AccessTools.FieldRefAccess<FakeHooks.FakeGame, System.Int32>(\"m_kind\"): the field is FakeHooks.FakeKind",
            "Broken.MoreNames: AccessTools.FieldRefAccess<FakeHooks.FakeGame, System.Int32>(\"s_count\"): the field is static",
            "Broken.MoreNames: AccessTools.FieldRefAccess<FakeHooks.FakeGame, FakeHooks.IFakeSeq`1<System.Int32>>(\"m_seq\"): the field is FakeHooks.FakeSeq`1<System.String>",
            "Broken.MoreNames: AccessTools.FieldRefAccess<FakeHooks.FakeGame, FakeHooks.FakeGameBase>(\"m_arr\"): the field is FakeHooks.FakeGame[]",
            "Broken.MoreNames: AccessTools.FieldRefAccess<FakeHooks.FakeStruct, System.Int32>(\"m_x\"): T can't be a struct",
            "Broken.MoreNames: AccessTools.FieldRefAccess<System.Int32>(typeof(FakeHooks.FakeStruct), \"m_x\"): an instance field of a struct",
            "Broken.MoreNames: AccessTools.StaticFieldRefAccess<FakeHooks.FakeGame, System.Int32>(\"m_own\"): the field isn't static",
            "Broken.MoreNames: AccessTools.Method(typeof(FakeHooks.FakeGame), \"Damage\", [System.String]) not found",
            "Broken.MoreNames: AccessTools.Method(typeof(FakeHooks.FakeGame), \"Use\"): not generic with 1 type parameter(s)",
            "Broken.MoreNames: AccessTools.TypeByName(\"FakeHooks.NoSuchType\") not found",
            "Broken.MoreNames: AccessTools.TypeByName(\"Twin\") matches several types",
            "Broken.MoreNames: AccessTools.Inner(typeof(FakeHooks.FakeGame), \"NoSuchNested\") not found",
            "Broken.PassedOn: AccessTools.MethodDelegate<System.Func`2<FakeHooks.FakeGame,System.Int32>>: doesn't fit FakeHooks.FakeGame.Deleg",
            "Broken.PassedOn: AccessTools.MethodDelegate<System.Action`1<FakeHooks.FakeGame>>: doesn't fit FakeHooks.FakeGame.Use",
            "Broken.PassedOn: AccessTools.StaticFieldRefAccess<System.Int32>(FakeHooks.FakeGame.m_own): the field isn't static",
            "Broken.PassedOn: AccessTools.FieldRefAccess<FakeHooks.FakeGame, System.String>(FakeHooks.FakeGame.m_own): the field is System.Int32",
            "Broken.Guarded: AccessTools.FieldRefAccess<FakeHooks.FakeGame, System.Int32>(\"m_noSuchGuarded\") not found",
            "Broken.Opaque: AccessTools.StaticFieldRefAccess<System.Int32>(typeof(FakeHooks.FakeGame), \"c_const\"): the field is a constant",
            "Broken.Opaque: AccessTools.FieldRefAccess<FakeHooks.FakeGame, System.Int32>(FakeHooks.FakeGame.s_count): the field is static",
            "Broken.Opaque: AccessTools.MethodDelegate on FakeHooks.IFake.Bar: an interface method needs virtualCall: true",
            "UnannotatedDynamic: [HarmonyTargetMethod] / TargetMethod(s) isn't read by the hook check",
            "Parameters.FieldWrongType: ___m_inherited is System.String, the field is System.Int32",
            "Parameters.WrongName: parameter wrongName isn't one of FakeHooks.FakeGame.Use's",
            "Parameters.WrongType: parameter v is System.Int32, FakeHooks.FakeGame.Use's is System.Single",
            "Parameters.ResultOfVoid: __result, but FakeHooks.FakeGame.Use returns nothing",
            "Parameters.ResultWrongType: __result is System.Int32, FakeHooks.FakeGame.Have returns System.Boolean",
            "Parameters.MissingField: ___m_noSuchField: no field m_noSuchField on FakeHooks.FakeGame",
            "Parameters.BadIndex: __1: FakeHooks.FakeGame.Use has 1 parameter(s)",
            "Parameters.UnknownSpecial: __foo isn't a parameter name HarmonyX knows",
            "Parameters.ArgsWrongType: __args must be object[]",
            "Parameters.ExceptionInPostfix: __exception is only set in a finalizer",
            "Parameters.PrefixReturnsInt: a prefix returns void or bool, not System.Int32",
            "Parameters.PassThroughWrong: a postfix returning System.Single must take it as its first parameter",
            "Parameters.Renamed: [HarmonyArgument] isn't read by the hook check",
            "ClassLevel.Prefix: FakeHooks.FakeGame.NoSuchMethodEither not found",
            "ClassLevelTwo.Prefix: FakeHooks.FakeGame.NoSuchA not found",
            "Unannotated.Stray: no target (a type and a method name)",
            "Dynamic: [HarmonyTargetMethod] / TargetMethod(s) isn't read by the hook check",
            "NotStatic.Prefix: hooks must be static",
        };
        var report = FakeReport();
        var all = string.Join("\n  ", report.Problems);
        foreach (var e in expected)
            Eq(1, report.Problems.Count(p => p.StartsWith(e, StringComparison.Ordinal)), $"reported once: {e}\n  got:\n  {all}");
        Eq(expected.Length, report.Problems.Count, $"only the broken ones\n  got:\n  {all}");
    }

    private static void Test_HookCheck_FakesThatAreFine()
    {
        var report = FakeReport();
        foreach (var fine in new[]
                 {
                     "FakeHooks.FakeGame.Block", // Broken.Two's first target, GoodClassLevel / ClassLevelTwo / Unannotated, AccessTools.Method
                     "FakeHooks.FakeGame.Damage(System.Int32, System.Single)", "FakeHooks.FakeGame.Damage", // Broken.Overload, AccessTools.Method
                     "FakeHooks.FakeGame.m_own", "FakeHooks.FakeGame.m_inherited",
                     "FakeHooks.FakeGame.Use", "FakeHooks.FakeGame.Have", "FakeHooks.FakeGame.Power", // Parameters.Fine / FineLoose / PassThrough
                     "FakeHooks.FakeGame.Items", "FakeHooks.FakeGame.Tiny", "FakeHooks.FakeVirtual.Baz", // InterfaceResult, ReverseTiny, Virtual
                     // Broken.MoreNames / PassedOn: members only those cases touch
                     "FakeHooks.FakeGame.m_refOnly", "FakeHooks.FakeGame.m_inheritedRef", "FakeHooks.FakeGame.m_instOnly",
                     "FakeHooks.FakeGame.m_kind", "FakeHooks.FakeGame.m_seq", "FakeHooks.FakeGame.m_arr", "FakeHooks.FakeStruct.s_y",
                     "FakeHooks.FakeGame.Power(System.Int32)", "FakeHooks.FakeGame.Block()",
                     "FakeHooks.FakeGame.Load(System.Byte[], System.Collections.Generic.List`1<System.Int32>)", "FakeHooks.FakeGame.Gen",
                     "FakeHooks.FakeOther", "FakeHooks.FakeOther.Only", "FakeHooks.FakeGame.Nested", "FakeHooks.FakeGame.Nested.m_x",
                     "FakeHooks.FakeGame.Deleg as System.Action`1<FakeHooks.FakeGame>",
                     "FakeHooks.FakeGame.Deleg2 as System.Action`1<FakeHooks.FakeGame>",
                     "FakeHooks.FakeGame.StaticCount as System.Func`1<System.Int32>",
                     "FakeHooks.FakeGame.m_staticOnly as System.Int32", "FakeHooks.FakeGame.m_guarded",
                     "FakeHooks.FakeGame.Power2(System.Int32)", "FakeHooks.FakeGame.Block2",
                 })
            True(report.Checked.Contains(fine), $"checked: {fine} (got {string.Join(", ", report.Checked)})");
        Eq(string.Join(" | ", new[]
           {
               "Broken.Names: AccessTools.Field with a name built at runtime (or arguments the check doesn't read)",
               "Broken.Names: AccessTools.Property isn't read by the hook check",
               "Broken.MoreNames: AccessTools.Field with arguments that branch (?:, ??)",
               "Broken.MoreNames: AccessTools.Field with arguments that branch (?:, ??)",
               "Broken.MoreNames: AccessTools.Field with a name built at runtime (or arguments the check doesn't read)",
               "Broken.MoreNames: AccessTools.StaticFieldRefAccess with a name built at runtime (or arguments the check doesn't read)",
               "Broken.Opaque: AccessTools.Method with a name built at runtime (or arguments the check doesn't read)",
               "Broken.Opaque: AccessTools.Field with a name built at runtime (or arguments the check doesn't read)",
               "Broken.Opaque: AccessTools.Field with a name built at runtime (or arguments the check doesn't read)",
               "Broken.Opaque: AccessTools.Field with a name built at runtime (or arguments the check doesn't read)",
               "Broken.Opaque: AccessTools.FieldRefAccess with a name built at runtime (or arguments the check doesn't read)",
               "Broken.Opaque: AccessTools.MethodDelegate on a generic method",
               "Broken.Escapes: Traverse.Create isn't read by the hook check",
               "Broken.Escapes: Traverse.Field isn't read by the hook check",
               "Broken.Escapes: CodeInstruction.Call isn't read by the hook check",
               "Broken.Escapes: HarmonyMethod..ctor isn't read by the hook check",
               "Broken.Escapes: Harmony.Patch isn't read by the hook check",
               "Broken.Escapes: Harmony.PatchAll isn't read by the hook check",
           }), string.Join(" | ", report.Skipped), "skipped");
    }

    // The guard: every [HarmonyPatch] hook and every AccessTools call the check finds must end up checked, reported or
    // skipped, so a form it stops reading can't make it pass by checking nothing.
    private static void Test_HookCheck_EveryHookAndLookupIsRead()
    {
        var report = FakeReport();
        Eq("", string.Join(" | ", report.Problems.Where(p => p.Contains("the hook check didn't read"))), "unread");
        True(report.HooksSeen > 30 && report.LookupsSeen > 50, $"seen: {report.HooksSeen} hooks, {report.LookupsSeen} lookups");
        Eq("Broken.Missing", HookCheck.Unread(new[] { "Broken.Missing", "Broken.Tiny" }, new[] { "Broken.Tiny" }).Single(), "Unread");
    }

    // The one self-test that uses the game (calls between game DLLs can't be faked): if it fails, the plugin's hooks are
    // fine (HookCheck_PluginHooksMatchTheGame checks them); only this example changed in the game.
    private static void Test_HookCheck_SeesCallsFromOtherGameDlls()
    {
        var report = HookCheck.Run(typeof(Tests).Assembly.Location, ManagedDir, t => t.Namespace == "GameExampleHooks");
        True(report.Problems.Count == 1 && report.Problems[0].StartsWith("GameExample.OtherModule: GuiBar.GetSmoothValue is ", StringComparison.Ordinal),
            "the hook check's own example (not the plugin's hooks): GuiBar.GetSmoothValue should be tiny and called from " +
            "assembly_valheim; if the game changed it, pick another such method. Got: " + string.Join(" | ", report.Problems));
    }

    private static void Test_HookCheck_PluginHooksMatchTheGame()
    {
        var report = HookCheck.Run(PluginDll, ManagedDir);
        Eq(0, report.Problems.Count, "hook problems (game update?):\n  " + string.Join("\n  ", report.Problems));
        // Every lookup read: a new one the check can't read must be taught to it (or the name made fixed).
        Eq(0, report.Skipped.Count, "skipped:\n  " + string.Join("\n  ", report.Skipped));
        System.Console.WriteLine($"  hook check: {report.Checked.Distinct().Count()} game members checked in {System.IO.Path.GetFullPath(PluginDll)}");
    }

    private static void Test_HookCheck_MissingPluginDllFails()
    {
        var path = PluginDll + ".missing";
        try
        {
            HookCheck.Run(path, ManagedDir);
        }
        catch (Exception e) when (e.Message.Contains(path) && e.Message.Contains("make test"))
        {
            return;
        }
        throw new Exception("a missing plugin DLL must fail the check and name the path");
    }
}
