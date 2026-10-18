using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

/// <summary>
/// Checks a mod DLL's game hooks against the game DLLs offline: both are read as files with Mono.Cecil (nothing is
/// loaded or run, no Unity). It follows HarmonyX 2.9 (what BepInExPack 5.4.23 ships; the mods compile against 2.7.0,
/// which finds targets and binds parameters the same way but for <c>__args</c>), so a hook that wouldn't install in game
/// is reported here, all at once:
/// <list type="bullet">
/// <item>every <c>[HarmonyPatch]</c> (class and method level merged as AttributePatch.Create does) must find exactly
/// one method declared on that type with that name and, when given, those parameter types (PatchTools.GetOriginalMethod
/// → AccessTools.DeclaredMethod). Forms it doesn't read (MethodType, ArgumentType[], TargetMethod, HarmonyArgument)
/// are reported, never skipped;</item>
/// <item>hook parameters must bind to the target as HarmonyManipulator binds them: by the target's parameter names,
/// <c>__N</c>, <c>__instance</c>, <c>__result</c>, <c>___field</c>, …; hooks must be static, a prefix returns void or
/// bool, a postfix returning a value takes it first. A parameter of the wrong type is reported too: Harmony installs
/// it, but it would mix up types at runtime;</item>
/// <item>a target the game calls directly that Mono can inline there (20 bytes of IL or less, or AggressiveInlining;
/// called with <c>call</c>, or not virtual / final / in a sealed class) is reported: inlined calls skip the patch.
/// Calls in every <c>assembly_*.dll</c> count (Mono inlines across assemblies);</item>
/// <item>AccessTools lookups with fixed names must find their member, as HarmonyX does: <c>Field / DeclaredField /
/// Method / DeclaredMethod(typeof(T), "name"[, new[] { typeof(A) }[, generic arguments]])</c>,
/// <c>FieldRefAccess / StaticFieldRefAccess</c> by name or on a FieldInfo from a lookup (static-ness, struct rules,
/// the field's type), <c>MethodDelegate</c> on a MethodInfo from a lookup (the delegate's signature), <c>TypeByName</c>,
/// <c>Inner</c>; also nested (<c>Field(Inner(…), …)</c>) or through a local set once. Arguments that branch (?:, ??),
/// names built at runtime and other AccessTools calls are listed in <see cref="Report.Skipped"/>.</item>
/// </list>
/// Harmony calls that look up or patch outside AccessTools and [HarmonyPatch] (Traverse, CodeInstruction.Call,
/// new HarmonyMethod(type, "name"), Harmony.Patch, a PatchAll that isn't PatchAll(typeof(X))) are listed as skipped.
/// A guard reports any hook or AccessTools call it found but didn't read. Not covered: a game method that keeps its shape
/// but changes what it does; which classes the mod passes to PatchAll; plain reflection (Type.GetMethod).
/// The same file in each of these Valheim mod repos: a change goes into all of them.
/// </summary>
internal static class HookCheck
{
    internal sealed class Report
    {
        public readonly List<string> Problems = new List<string>();
        /// <summary>The game members found, e.g. <c>Humanoid.BlockAttack</c>, <c>ItemDrop.ItemData.GetDamage(System.Int32, System.Single)</c>,
        /// <c>Humanoid.m_leftItem</c>, <c>Character.GetNextOrCurrentAnimHash as System.Func`2&lt;Character, System.Int32&gt;</c>.</summary>
        public readonly List<string> Checked = new List<string>();
        public readonly List<string> Skipped = new List<string>();
        /// <summary>How many hooks and AccessTools calls the check found (the guard compares them with what it read).</summary>
        public int HooksSeen, LookupsSeen;
        internal readonly HashSet<string> ReadHooks = new HashSet<string>();
        internal int LookupsRead;
    }

    private const int InlineLimit = 20; // Mono inlines methods under 20 bytes of IL; 20 itself counts too, to be safe
    private const string AccessToolsType = "HarmonyLib.AccessTools";
    private static readonly string[] PatchKinds = { "Prefix", "Postfix", "Transpiler", "Finalizer", "ReversePatch", "ILManipulator" };
    private static readonly string[] Dynamic = { "HarmonyLib.HarmonyTargetMethod", "HarmonyLib.HarmonyTargetMethods" };
    private static readonly string[] MemberLookups = { "Field", "DeclaredField", "Method", "DeclaredMethod" };

    /// <summary>Methods called directly (call / callvirt) anywhere in the game's own DLLs and the checked one.</summary>
    private sealed class Calls
    {
        public readonly HashSet<string> Any = new HashSet<string>();
        public readonly HashSet<string> NonVirtual = new HashSet<string>(); // with `call`: base.M(), static and struct calls
    }

    /// <param name="include">Which top-level types to check (all when null).</param>
    public static Report Run(string assemblyPath, string managedDir, Func<TypeDefinition, bool> include = null)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"{assemblyPath} not found: run `make test` (it builds the plugin first)", assemblyPath);
        var report = new Report();
        var module = Read(assemblyPath, managedDir);
        var calls = new Lazy<Calls>(() => DirectCalls(Directory.GetFiles(managedDir, "assembly_*.dll")
            .Select(dll => Read(dll, managedDir)).Append(module)));
        // For TypeByName: every DLL of the game, in name order (deterministic), then the checked one.
        var types = new Lazy<List<TypeDefinition>>(() => Directory.GetFiles(managedDir, "*.dll").OrderBy(f => f, StringComparer.Ordinal)
            .Select(dll => TryRead(dll, managedDir)).Where(m => m != null).Append(module).SelectMany(m => m.GetTypes()).ToList());
        var hooks = new List<string>();
        foreach (var type in module.GetTypes().Where(t => include == null || include(Outermost(t))))
        {
            hooks.AddRange(HookMethods(type).Select(m => $"{type.Name}.{m.Name}"));
            CheckPatches(type, report, calls);
            foreach (var method in type.Methods.Where(m => m.HasBody))
                CheckNames(method, report, types);
        }
        // The guard: a hook or lookup the check found but didn't read means it no longer understands a form.
        report.HooksSeen = hooks.Count;
        foreach (var who in Unread(hooks, report.ReadHooks).Distinct())
            report.Problems.Add($"{who}: the hook check didn't read this hook (teach it this form)");
        if (report.LookupsRead != report.LookupsSeen)
            report.Problems.Add($"the hook check didn't read {report.LookupsSeen - report.LookupsRead} AccessTools call(s) (teach it this form)");
        return report;
    }

    /// <summary>What the guard reports: the hooks found but not read.</summary>
    public static IEnumerable<string> Unread(IEnumerable<string> found, IEnumerable<string> read)
    {
        var done = new HashSet<string>(read);
        return found.Where(f => !done.Contains(f));
    }

    public static MethodDefinition GameMethod(string managedDir, string type, string name, params string[] parameterTypes) =>
        Method(Path.Combine(managedDir, "assembly_valheim.dll"), type, name, parameterTypes);

    /// <param name="type">Cecil's full name: nested types with a slash, <c>ItemDrop/ItemData</c>.</param>
    public static MethodDefinition Method(string assemblyPath, string type, string name, params string[] parameterTypes)
    {
        var found = Read(assemblyPath, Path.GetDirectoryName(assemblyPath)).GetType(type)?.Methods
            .Where(m => m.Name == name && (parameterTypes.Length == 0 || ParameterTypes(m).SequenceEqual(parameterTypes))).ToList();
        if (found == null || found.Count != 1)
            throw new Exception($"{type}.{name}: {(found == null ? "no such type" : found.Count + " matches")} in {assemblyPath}");
        return found[0];
    }

    private static ModuleDefinition TryRead(string path, string managedDir)
    {
        try
        {
            return Read(path, managedDir);
        }
        catch (BadImageFormatException)
        {
            return null; // not a .NET DLL
        }
    }

    private static ModuleDefinition Read(string path, string managedDir)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var dir in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(dir);
        resolver.AddSearchDirectory(managedDir); // the game's DLLs first: the test folder has copies of some
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        return ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
    }

    // ---- [HarmonyPatch] targets

    /// <summary>What one or more <c>[HarmonyPatch]</c> attributes say.</summary>
    private sealed class Target
    {
        public TypeReference Type;
        public string Name;
        public string[] Args; // null: not given
        public string Unsupported; // the attribute form, when the check doesn't read it
    }

    /// <summary>HarmonyMethod.Merge: later attributes override what the earlier ones set.</summary>
    private static Target Merge(params Target[] lowToHigh) => lowToHigh.Where(t => t != null).Aggregate(new Target(), (under, t) => new Target
    {
        Type = t.Type ?? under.Type, Name = t.Name ?? under.Name, Args = t.Args ?? under.Args, Unsupported = t.Unsupported ?? under.Unsupported,
    });

    /// <summary>AttributePatch.Create's Same: one target, the same type, name and parameter types.</summary>
    private static bool Same(Target a, Target b) =>
        a.Type?.FullName == b.Type?.FullName && a.Name == b.Name && (a.Args ?? new string[0]).SequenceEqual(b.Args ?? new string[0]);

    private static bool IsHarmonyPatch(CustomAttribute a) => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch";

    private static Target ReadTarget(CustomAttribute attr)
    {
        var kinds = attr.Constructor.Parameters.Select(p => p.ParameterType.FullName).ToList();
        if (kinds.Distinct().Count() != kinds.Count || kinds.Any(k => k != "System.Type" && k != "System.String" && k != "System.Type[]"))
            return new Target { Unsupported = $"[HarmonyPatch({string.Join(", ", kinds)})]" };
        var target = new Target();
        for (var i = 0; i < kinds.Count; i++)
        {
            var value = attr.ConstructorArguments[i].Value;
            if (kinds[i] == "System.Type") target.Type = (TypeReference)value;
            else if (kinds[i] == "System.String") target.Name = (string)value;
            else target.Args = ((CustomAttributeArgument[])value)?.Select(a => ((TypeReference)a.Value).FullName).ToArray() ?? new string[0];
        }
        return target;
    }

    /// <summary>AttributePatch.GetPatchType: named Prefix, Postfix, … or marked [HarmonyPrefix], [HarmonyPostfix], …; null if neither.</summary>
    private static string PatchKind(MethodDefinition m) =>
        PatchKinds.FirstOrDefault(k => m.Name == k || m.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.Harmony" + k));

    private static bool Has(ICustomAttributeProvider p, string attribute) => p.CustomAttributes.Any(a => a.AttributeType.FullName == attribute);

    /// <summary>A hook class: [HarmonyPatch] on the class or one of its methods, or a runtime-picked target. A class
    /// without [HarmonyPatch] is still patched by harmony.PatchAll(typeof(X)) (allowUnannotatedType).</summary>
    private static bool IsHookClass(TypeDefinition type) =>
        type.CustomAttributes.Any(IsHarmonyPatch) || type.Methods.Any(m => m.CustomAttributes.Any(IsHarmonyPatch) || Dynamic.Any(d => Has(m, d))) ||
        (type.Methods.Any(IsTargetMethod) && type.Methods.Any(m => PatchKind(m) != null));

    /// <summary>PatchTools.GetPatchMethod: the auxiliary TargetMethod(s), by name or attribute.</summary>
    private static bool IsTargetMethod(MethodDefinition m) => m.Name == "TargetMethod" || m.Name == "TargetMethods" || Dynamic.Any(d => Has(m, d));

    /// <summary>The guard's list: methods with [HarmonyPatch], and the hook kinds (Prefix, …) in a hook class.</summary>
    private static IEnumerable<MethodDefinition> HookMethods(TypeDefinition type) =>
        IsHookClass(type) ? type.Methods.Where(m => m.CustomAttributes.Any(IsHarmonyPatch) || PatchKind(m) != null) : Enumerable.Empty<MethodDefinition>();

    private static void CheckPatches(TypeDefinition type, Report report, Lazy<Calls> calls)
    {
        if (!IsHookClass(type)) return;
        var classAttributes = type.CustomAttributes.Where(IsHarmonyPatch).Select(ReadTarget).ToList();
        var dynamic = type.Methods.Any(IsTargetMethod);
        var container = Merge(classAttributes.ToArray());
        if (container.Unsupported != null || dynamic || Has(type, "HarmonyLib.HarmonyPatchAll") || Has(type, "HarmonyLib.HarmonyArgument"))
        {
            report.Problems.Add($"{type.Name}: " + (container.Unsupported ?? (dynamic ? "[HarmonyTargetMethod] / TargetMethod(s)" :
                Has(type, "HarmonyLib.HarmonyPatchAll") ? "[HarmonyPatchAll]" : "[HarmonyArgument]")) + " isn't read by the hook check");
            foreach (var m in HookMethods(type)) report.ReadHooks.Add($"{type.Name}.{m.Name}"); // covered by that report
            return;
        }

        foreach (var method in type.Methods)
        {
            var attributes = method.CustomAttributes.Where(IsHarmonyPatch).Select(ReadTarget).ToList();
            var who = $"{type.Name}.{method.Name}";
            var kind = PatchKind(method);
            if (kind == null)
            {
                if (attributes.Count == 0) continue;
                report.Problems.Add($"{who}: has [HarmonyPatch] but Harmony won't run it (no [HarmonyPrefix], [HarmonyPostfix], …, " +
                                    "and not named Prefix, Postfix, …)");
                report.ReadHooks.Add(who);
                continue;
            }
            report.ReadHooks.Add(who); // every path below reports or checks
            if (!method.IsStatic && kind != "ReversePatch")
            {
                report.Problems.Add($"{who}: hooks must be static");
                continue;
            }
            var unsupported = attributes.FirstOrDefault(a => a.Unsupported != null)?.Unsupported ??
                              (Has(method, "HarmonyLib.HarmonyArgument") ? "[HarmonyArgument]" : null);
            if (unsupported != null)
            {
                report.Problems.Add($"{who}: {unsupported} isn't read by the hook check");
                continue;
            }
            // AttributePatch.Create (collectIncomplete = the class names a type): attributes that complete a target are
            // separate patches when there are several; the rest fill in for each. PatchClassProcessor then merges the class's.
            var classHasType = container.Type != null;
            bool Complete(Target a) => (classHasType || a.Type != null) && a.Name != null;
            var filler = Merge(attributes.Where(a => !Complete(a)).ToArray());
            var complete = attributes.Where(Complete).Where(a => !Same(a, filler)).ToList();
            var targets = complete.Count > 1
                ? complete.Select(c => Merge(container, filler, c))
                : new[] { Merge(new[] { container }.Concat(attributes).ToArray()) };
            foreach (var target in targets)
                CheckTarget(who, method, kind, target, report, calls);
        }
    }

    private static void CheckTarget(string who, MethodDefinition patch, string kind, Target target, Report report, Lazy<Calls> calls)
    {
        if (target.Type == null || target.Name == null)
        {
            report.Problems.Add($"{who}: no target (a type and a method name)");
            return;
        }
        var type = Resolve(target.Type);
        if (type == null)
        {
            report.Problems.Add($"{who}: type {Show(target.Type)} not found in the game");
            return;
        }
        var shown = $"{Show(type)}.{target.Name}" + (target.Args == null ? "" : $"({string.Join(", ", target.Args)})");
        var found = type.Methods.Where(m => m.Name == target.Name && (target.Args == null || ParameterTypes(m).SequenceEqual(target.Args))).ToList();
        if (found.Count == 0)
        {
            report.Problems.Add($"{who}: {shown} not found in the game");
            return;
        }
        if (found.Count > 1)
        {
            report.Problems.Add($"{who}: {shown} is overloaded: give the parameter types");
            return;
        }
        report.Checked.Add(shown);
        var method = found[0];
        if (kind == "Prefix" || kind == "Postfix" || kind == "Finalizer") // the others take their parameters by type
            CheckParameters(who, kind, patch, method, report);
        if (kind != "ReversePatch") // a reverse patch is a copy of the original: inlining at the game's calls doesn't matter
            CheckInlining(who, shown, method, report, calls);
    }

    /// <summary>HarmonyManipulator (WritePrefixes / WritePostfixes / EmitCallParameter): the hook's return type, and how
    /// each of its parameters is filled in from the target.</summary>
    private static void CheckParameters(string who, string kind, MethodDefinition patch, MethodDefinition target, Report report)
    {
        var targetName = $"{Show(target.DeclaringType)}.{target.Name}";
        var returns = patch.ReturnType.FullName;
        var parameters = patch.Parameters.ToList();
        if (kind == "Prefix" && returns != "System.Void" && returns != "System.Boolean")
            report.Problems.Add($"{who}: a prefix returns void or bool, not {returns}");
        if (kind == "Postfix" && returns != "System.Void")
        {
            // A pass-through postfix: its first parameter is the result (exactly its return type), not the target's.
            if (parameters.Count == 0 || parameters[0].ParameterType.FullName != returns)
                report.Problems.Add($"{who}: a postfix returning {returns} must take it as its first parameter");
            else
                parameters.RemoveAt(0);
        }
        foreach (var parameter in parameters)
        {
            var name = parameter.Name;
            var type = ElementType(parameter.ParameterType);
            if (Has(parameter, "HarmonyLib.HarmonyArgument"))
            {
                report.Problems.Add($"{who}: [HarmonyArgument] isn't read by the hook check (on {name})");
                continue;
            }
            if (name == "__originalMethod" || name == "__state") continue;
            if (name == "__runOriginal")
            {
                if (kind == "Finalizer") report.Problems.Add($"{who}: __runOriginal is only set for a prefix or postfix");
                continue;
            }
            if (name == "__exception")
            {
                if (kind != "Finalizer") report.Problems.Add($"{who}: __exception is only set in a finalizer");
                continue;
            }
            if (name == "__args")
            {
                if (parameter.ParameterType.FullName != "System.Object[]") report.Problems.Add($"{who}: __args must be object[]");
                continue;
            }
            if (name == "__instance")
            {
                if (!target.IsStatic && !IsSameOrBase(type, target.DeclaringType))
                    report.Problems.Add($"{who}: __instance is {type.FullName}, {targetName} is on {target.DeclaringType.FullName}");
                continue;
            }
            if (name.StartsWith("___", StringComparison.Ordinal))
            {
                var fieldName = name.Substring(3);
                var field = fieldName.All(char.IsDigit) ? null : FindField(target.DeclaringType, fieldName, declaredOnly: false);
                if (fieldName.All(char.IsDigit))
                    report.Problems.Add($"{who}: {name}: fields by index aren't read by the hook check");
                else if (field == null)
                    report.Problems.Add($"{who}: {name}: no field {fieldName} on {Show(target.DeclaringType)}");
                // Harmony loads the field as it is (ldfld, no box): a value type must match exactly.
                else if (IsValueType(field.FieldType) ? type.FullName != field.FieldType.FullName : !IsSameOrBase(type, field.FieldType))
                    report.Problems.Add($"{who}: {name} is {type.FullName}, the field is {field.FieldType.FullName}");
                continue;
            }
            if (name == "__result")
            {
                if (target.ReturnType.FullName == "System.Void")
                    report.Problems.Add($"{who}: __result, but {targetName} returns nothing");
                else if (!IsSameOrBase(type, ElementType(target.ReturnType)))
                    report.Problems.Add($"{who}: __result is {type.FullName}, {targetName} returns {target.ReturnType.FullName}");
                continue;
            }
            int index;
            if (name.StartsWith("__", StringComparison.Ordinal))
            {
                if (!int.TryParse(name.Substring(2), out index))
                {
                    report.Problems.Add($"{who}: {name} isn't a parameter name HarmonyX knows");
                    continue;
                }
                if (index < 0 || index >= target.Parameters.Count)
                {
                    report.Problems.Add($"{who}: {name}: {targetName} has {target.Parameters.Count} parameter(s)");
                    continue;
                }
            }
            else
            {
                index = target.Parameters.Select(p => p.Name).ToList().IndexOf(name);
                if (index < 0)
                {
                    report.Problems.Add($"{who}: parameter {name} isn't one of {targetName}'s " +
                                        $"({string.Join(", ", target.Parameters.Select(p => p.Name))}): renamed in the game?");
                    continue;
                }
            }
            var targetType = ElementType(target.Parameters[index].ParameterType);
            if (!IsSameOrBase(type, targetType))
                report.Problems.Add($"{who}: parameter {name} is {type.FullName}, {targetName}'s is {targetType.FullName}");
        }
    }

    private static void CheckInlining(string who, string shown, MethodDefinition method, Report report, Lazy<Calls> calls)
    {
        if (!method.HasBody || method.NoInlining || (!method.AggressiveInlining && method.Body.CodeSize > InlineLimit)) return;
        // A callvirt reaches a virtual method only through the vtable, unless Mono devirtualizes it (final, sealed class);
        // `call` (base.M()) is direct either way.
        var devirtualized = !method.IsVirtual || method.IsFinal || method.DeclaringType.IsSealed;
        if (!calls.Value.NonVirtual.Contains(method.FullName) && !(devirtualized && calls.Value.Any.Contains(method.FullName))) return;
        report.Problems.Add(method.AggressiveInlining
            ? $"{who}: {shown} is marked AggressiveInlining and called directly in the game: Mono can inline it there, and those calls skip the patch"
            : $"{who}: {shown} is {method.Body.CodeSize} bytes of IL and called directly in the game: Mono can inline it there, and those calls skip the patch");
    }

    /// <summary>Every method called with call / callvirt in these modules (engine messages like Awake and RPC handlers
    /// aren't). Calls through a generic instance (<c>List&lt;T&gt;</c> methods) don't match the definition's name.</summary>
    private static Calls DirectCalls(IEnumerable<ModuleDefinition> modules)
    {
        var calls = new Calls();
        foreach (var i in modules.SelectMany(m => m.GetTypes()).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions))
        {
            if (!(i.Operand is MethodReference called) || (i.OpCode != OpCodes.Call && i.OpCode != OpCodes.Callvirt)) continue;
            calls.Any.Add(called.FullName);
            if (i.OpCode == OpCodes.Call) calls.NonVirtual.Add(called.FullName);
        }
        return calls;
    }

    // ---- AccessTools lookups with fixed names

    /// <summary>What the check knows about one argument of a lookup, read back from the IL before the call.</summary>
    private abstract class Arg { }

    private sealed class UnknownArg : Arg { }

    private sealed class NullArg : Arg { }

    private sealed class IntArg : Arg
    {
        public int Value;
    }

    private sealed class StringArg : Arg
    {
        public string Value;
    }

    /// <summary>typeof(T), or what AccessTools.TypeByName / Inner returned (<c>Type</c> null when that lookup failed:
    /// it's reported at its own call).</summary>
    private sealed class TypeArg : Arg
    {
        public TypeReference Ref; // as written: System.Byte[], List`1<System.Int32> (resolving those gives Byte, List`1)
        public TypeDefinition Type;
        public string Shown;
    }

    /// <summary><c>new[] { typeof(A), … }</c>, <c>Type.EmptyTypes</c>: the type names.</summary>
    private sealed class TypesArg : Arg
    {
        public string[] Names;
    }

    /// <summary>A FieldInfo / MethodInfo from a lookup (directly or through a local set once). Both null: that lookup
    /// failed and is reported at its own call.</summary>
    private sealed class MemberArg : Arg
    {
        public FieldDefinition Field;
        public MethodDefinition Method;
    }

    /// <summary>One method being scanned: which instruction produced each argument of its calls (Sources; null when the
    /// method's IL isn't followed) and the types TypeByName searches.</summary>
    private sealed class Scan
    {
        public MethodDefinition Method;
        public Dictionary<Instruction, Instruction[]> Sources;
        public Lazy<List<TypeDefinition>> Types;
    }

    /// <summary>An argument fed by different values on different paths (?:, ??).</summary>
    private sealed class BranchArg : Arg { }

    /// <summary>A lookup's result: what it found (TypeDefinition / FieldDefinition / MethodDefinition, null if it failed
    /// or wasn't read) and why it was skipped (null if it was read).</summary>
    private sealed class Outcome
    {
        public object Found;
        public string Skipped;
    }

    private const string Unreadable = "with a name built at runtime (or arguments the check doesn't read)";
    private const string Branching = "with arguments that branch (?:, ??)";

    private static void CheckNames(MethodDefinition method, Report report, Lazy<List<TypeDefinition>> types)
    {
        Scan scan = null;
        foreach (var call in method.Body.Instructions)
        {
            if (Escape(call) is string escape)
            {
                report.LookupsSeen++;
                report.Skipped.Add($"{method.DeclaringType.Name}.{method.Name}: {escape} isn't read by the hook check");
                report.LookupsRead++;
                continue;
            }
            if (!IsAccessToolsCall(call, out var called)) continue;
            scan = scan ?? new Scan { Method = method, Sources = Sources(method), Types = types };
            report.LookupsSeen++;
            var who = $"{method.DeclaringType.Name}.{method.Name}";
            var outcome = ReadLookup(who, call, called, report, scan);
            if (outcome.Skipped != null) report.Skipped.Add($"{who}: AccessTools.{called.Name} {outcome.Skipped}");
            report.LookupsRead++;
        }
    }

    /// <summary>A Harmony call that looks a member up by name or patches outside [HarmonyPatch] (Traverse,
    /// CodeInstruction.Call / LoadField / StoreField, new HarmonyMethod(type, "name"), Harmony.Patch / ReversePatch, a
    /// PatchAll that isn't PatchAll(typeof(X))): "Type.Method", or null.</summary>
    private static string Escape(Instruction i)
    {
        if (!(i.Operand is MethodReference m) || (i.OpCode != OpCodes.Call && i.OpCode != OpCodes.Callvirt && i.OpCode != OpCodes.Newobj)) return null;
        var type = m.DeclaringType.GetElementType().FullName;
        var kinds = m.Parameters.Select(p => p.ParameterType.FullName).ToList();
        var escapes =
            type.StartsWith("HarmonyLib.Traverse", StringComparison.Ordinal) ||
            (type == "HarmonyLib.CodeInstruction" && (m.Name == "Call" || m.Name == "CallClosure" || m.Name == "LoadField" || m.Name == "StoreField")) ||
            (type == "HarmonyLib.HarmonyMethod" && m.Name == ".ctor" && kinds.Contains("System.String")) ||
            (type == "HarmonyLib.Harmony" && (m.Name == "Patch" || m.Name == "CreateReversePatcher" || m.Name == "ReversePatch" ||
                                              (m.Name == "PatchAll" && !(kinds.Count == 1 && kinds[0] == "System.Type"))));
        return escapes ? $"{m.DeclaringType.Name.Split('`')[0]}.{m.Name}" : null;
    }

    private static bool IsAccessToolsCall(Instruction i, out MethodReference called)
    {
        called = i.Operand as MethodReference;
        return (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && called?.DeclaringType.FullName == AccessToolsType;
    }

    /// <summary>Reads a lookup call's arguments and checks it. <paramref name="report"/> gets its problems and what it
    /// found: the scan's own report for the call itself, a scratch one when it's an argument of another lookup.</summary>
    private static Outcome ReadLookup(string who, Instruction call, MethodReference called, Report report, Scan scan)
    {
        var args = Arguments(call, scan, 0);
        if (args == null) return new Outcome { Skipped = Unreadable };
        if (args.Any(a => a is BranchArg)) return new Outcome { Skipped = Branching };
        return CheckLookup(who, called, args, report, scan);
    }

    private static Arg[] Arguments(Instruction call, Scan scan, int depth) =>
        scan.Sources != null && scan.Sources.TryGetValue(call, out var sources) ? sources.Select(s => Value(s, scan, depth + 1)).ToArray() : null;

    /// <summary>The parameter kinds of an AccessTools overload: type names, method generic parameters as !!0, !!1.</summary>
    private static string Kinds(MethodReference called) =>
        string.Join(",", ((called as GenericInstanceMethod)?.ElementMethod ?? called).Parameters.Select(p => p.ParameterType.FullName));

    /// <summary>Checks one AccessTools lookup as HarmonyX 2.9 does it.</summary>
    private static Outcome CheckLookup(string who, MethodReference called, Arg[] args, Report report, Scan scan)
    {
        var generics = (called as GenericInstanceMethod)?.GenericArguments;
        var name = called.Name;
        var form = Kinds(called);
        if (name == "TypeByName" && form == "System.String")
        {
            if (!(args[0] is StringArg typeName)) return new Outcome { Skipped = Unreadable };
            var found = TypeByName(typeName.Value, scan.Types, out var several);
            if (several != null) report.Problems.Add($"{who}: AccessTools.TypeByName(\"{typeName.Value}\") matches several types ({several}): use the full name");
            else Found(found != null, found == null ? null : Show(found), $"{who}: AccessTools.TypeByName(\"{typeName.Value}\") not found in the game", report);
            return new Outcome { Found = several == null ? found : null };
        }
        if (name == "MethodDelegate" && form == "System.Reflection.MethodInfo,System.Object,System.Boolean")
            return CheckDelegate(who, generics?[0], args, report);
        if ((name == "FieldRefAccess" || name == "StaticFieldRefAccess") && form.Contains("System.Reflection.FieldInfo"))
            return CheckFieldInfo(who, name, generics, form, args, report);
        // The other lookups take (Type, string, …), or (string) / (T instance, string) with the type as T.
        TypeArg type;
        StringArg member;
        var typeAsT = false;
        if (form.StartsWith("System.Type,System.String", StringComparison.Ordinal))
        {
            type = args[0] as TypeArg;
            member = args[1] as StringArg;
            if (type == null || member == null || type.Ref is ArrayType || type.Ref is ByReferenceType || type.Ref is PointerType ||
                type.Ref is GenericInstanceType)
                return new Outcome { Skipped = Unreadable };
        }
        else if ((name == "FieldRefAccess" || name == "StaticFieldRefAccess") && generics?.Count == 2 && (form == "System.String" || form == "!!0,System.String"))
        {
            member = args[args.Length - 1] as StringArg;
            if (member == null || generics[0] is GenericInstanceType) return new Outcome { Skipped = Unreadable };
            type = new TypeArg { Ref = generics[0], Type = Resolve(generics[0]) };
            typeAsT = true;
            if (type.Type == null)
            {
                report.Problems.Add($"{who}: AccessTools.{name}: type {Show(generics[0])} not found in the game");
                return new Outcome();
            }
        }
        else
            return new Outcome { Skipped = "isn't read by the hook check" };
        if (type.Type == null)
        {
            if (type.Shown.StartsWith("typeof", StringComparison.Ordinal))
                report.Problems.Add($"{who}: AccessTools.{name}({type.Shown}, \"{member.Value}\"): type not found in the game");
            return new Outcome(); // an inner TypeByName / Inner failed: reported at its own call
        }
        var on = Show(type.Type);
        switch (name)
        {
            case "Inner":
                var nested = Inner(type.Type, member.Value);
                Found(nested != null, nested == null ? null : Show(nested), $"{who}: AccessTools.Inner({type.Shown}, \"{member.Value}\") not found in the game", report);
                return new Outcome { Found = nested };
            case "Field":
            case "DeclaredField":
                var plain = FindField(type.Type, member.Value, name == "DeclaredField");
                Found(plain != null, $"{on}.{member.Value}", $"{who}: AccessTools.{name}({type.Shown}, \"{member.Value}\") not found in the game", report);
                return new Outcome { Found = plain };
            case "Method":
            case "DeclaredMethod":
                string[] parameters = null;
                if (args.Length > 2 && args[2] is TypesArg types && types.Names.All(n => n != null)) parameters = types.Names;
                else if (args.Length > 2 && !(args[2] is NullArg)) return new Outcome { Skipped = Unreadable };
                int? genericCount = null;
                if (args.Length > 3 && args[3] is TypesArg genericTypes && genericTypes.Names.All(n => n != null)) genericCount = genericTypes.Names.Length;
                else if (args.Length > 3 && !(args[3] is NullArg)) return new Outcome { Skipped = Unreadable };
                var shown = $"AccessTools.{name}({type.Shown}, \"{member.Value}\"" + (parameters == null ? ")" : $", [{string.Join(", ", parameters)}])");
                var method = FindMethod(type.Type, member.Value, parameters, name == "DeclaredMethod", out var overloaded);
                if (overloaded) report.Problems.Add($"{who}: {shown} is overloaded: give the parameter types");
                else if (method == null) report.Problems.Add($"{who}: {shown} not found in the game");
                // MakeGenericMethod(generics) throws unless the method takes that many type parameters.
                else if (genericCount != null && method.GenericParameters.Count != genericCount)
                    report.Problems.Add($"{who}: {shown}: not generic with {genericCount} type parameter(s)");
                else
                {
                    report.Checked.Add($"{on}.{member.Value}" + (parameters == null ? "" : $"({string.Join(", ", parameters)})"));
                    return new Outcome { Found = method };
                }
                return new Outcome();
            case "FieldRefAccess":
            case "StaticFieldRefAccess":
                // AccessTools.GetInstanceField / StaticFieldRefAccessInternal + ValidateFieldType<F>.
                var wanted = generics?[generics.Count - 1];
                if (wanted == null) return new Outcome { Skipped = "isn't read by the hook check" };
                var shownRef = typeAsT
                    ? $"AccessTools.{name}<{on}, {Show(wanted)}>(\"{member.Value}\")"
                    : $"AccessTools.{name}<{Show(wanted)}>({type.Shown}, \"{member.Value}\")";
                var field = FindField(type.Type, member.Value, declaredOnly: false);
                if (field == null) report.Problems.Add($"{who}: {shownRef} not found in the game");
                else if (field.IsLiteral) report.Problems.Add($"{who}: {shownRef}: the field is a constant");
                else if (name == "StaticFieldRefAccess" && !field.IsStatic) report.Problems.Add($"{who}: {shownRef}: the field isn't static");
                else if (name == "FieldRefAccess" && typeAsT && type.Type.IsValueType) report.Problems.Add($"{who}: {shownRef}: T can't be a struct");
                else if (name == "FieldRefAccess" && typeAsT && field.IsStatic) report.Problems.Add($"{who}: {shownRef}: the field is static");
                else if (name == "FieldRefAccess" && !field.IsStatic && field.DeclaringType.IsValueType)
                    report.Problems.Add($"{who}: {shownRef}: an instance field of a struct");
                else if (!FieldTypeFits(wanted, field.FieldType)) report.Problems.Add($"{who}: {shownRef}: the field is {Show(field.FieldType)}");
                else report.Checked.Add($"{on}.{member.Value}");
                return new Outcome();
            default:
                return new Outcome { Skipped = "isn't read by the hook check" };
        }
    }

    /// <summary>(Static)FieldRefAccess on a FieldInfo from a lookup: AccessTools.FieldRefAccess&lt;T, F&gt;(FieldInfo) /
    /// StaticFieldRefAccessInternal&lt;F&gt;.</summary>
    private static Outcome CheckFieldInfo(string who, string name, Collection<TypeReference> generics, string form, Arg[] args, Report report)
    {
        var index = form.Split(',').ToList().IndexOf("System.Reflection.FieldInfo");
        var wanted = generics?[generics.Count - 1];
        if (wanted == null || !(args[index] is MemberArg member) || (member.Field == null && member.Method != null))
            return new Outcome { Skipped = Unreadable };
        if (member.Field == null) return new Outcome(); // the lookup failed: reported at its own call
        var field = member.Field;
        var t = generics.Count == 2 ? generics[0] : null;
        var shown = $"AccessTools.{name}<{(t == null ? "" : Show(t) + ", ")}{Show(wanted)}>({Show(field.DeclaringType)}.{field.Name})";
        var tDefinition = t == null ? null : Resolve(t);
        if (field.IsLiteral) report.Problems.Add($"{who}: {shown}: the field is a constant");
        else if (name == "StaticFieldRefAccess" && !field.IsStatic) report.Problems.Add($"{who}: {shown}: the field isn't static");
        else if (name == "FieldRefAccess" && index > 0 && field.IsStatic) report.Problems.Add($"{who}: {shown}: the field is static"); // (T instance, FieldInfo)
        else if (name == "FieldRefAccess" && tDefinition?.IsValueType == true) report.Problems.Add($"{who}: {shown}: T can't be a struct");
        else if (name == "FieldRefAccess" && !field.IsStatic && field.DeclaringType.IsValueType) report.Problems.Add($"{who}: {shown}: an instance field of a struct");
        // FieldRefNeedsClasscast: T and the field's class must be related ("instanceOfT is FieldDeclaringType" possible).
        else if (name == "FieldRefAccess" && !field.IsStatic && t != null && !IsSameOrBase(t, field.DeclaringType) && !IsSameOrBase(field.DeclaringType, t))
            report.Problems.Add($"{who}: {shown}: {Show(t)} and {Show(field.DeclaringType)} aren't related");
        else if (!FieldTypeFits(wanted, field.FieldType)) report.Problems.Add($"{who}: {shown}: the field is {Show(field.FieldType)}");
        else report.Checked.Add($"{Show(field.DeclaringType)}.{field.Name} as {Show(wanted)}");
        return new Outcome();
    }

    /// <summary>AccessTools.MethodDelegate&lt;D&gt;(method): a static method needs D's exact shape; an instance method
    /// takes the instance as D's first parameter.</summary>
    private static Outcome CheckDelegate(string who, TypeReference delegateType, Arg[] args, Report report)
    {
        if (delegateType == null || !(args[0] is MemberArg member) || (member.Method == null && member.Field != null) ||
            !(args[1] is NullArg) || !(args[2] is IntArg))
            return new Outcome { Skipped = Unreadable }; // a bound instance or a computed virtualCall isn't read
        if (member.Method == null) return new Outcome(); // the lookup failed: reported at its own call
        var method = member.Method;
        var shown = $"AccessTools.MethodDelegate<{Show(delegateType)}>: doesn't fit {Show(method.DeclaringType)}.{method.Name}" +
                    $"({string.Join(", ", ParameterTypes(method))})";
        if (method.HasGenericParameters) return new Outcome { Skipped = "on a generic method" };
        if (((IntArg)args[2]).Value == 0 && !method.IsStatic && method.DeclaringType.IsInterface)
        {
            report.Problems.Add($"{who}: AccessTools.MethodDelegate on {Show(method.DeclaringType)}.{method.Name}: an interface method needs virtualCall: true");
            return new Outcome();
        }
        if (!DelegateShape(delegateType, out var parameters, out var returns)) return new Outcome { Skipped = "with a delegate type the check doesn't read" };
        var expected = method.Parameters.Select(p => p.ParameterType).ToList();
        if (!method.IsStatic) expected.Insert(0, method.DeclaringType);
        var fits = parameters.Count == expected.Count &&
                   parameters.Zip(expected, (given, needed) => Assignable(needed, given)).All(x => x) &&
                   (method.ReturnType.FullName == "System.Void" ? returns == null : returns != null && Assignable(returns, method.ReturnType));
        if (fits) report.Checked.Add($"{Show(method.DeclaringType)}.{method.Name} as {Show(delegateType)}");
        else report.Problems.Add($"{who}: {shown}");
        return new Outcome();
    }

    /// <summary>Delegate variance: a value of <paramref name="from"/> passed as <paramref name="to"/> without boxing
    /// (value and by-ref types exactly, reference types to a base type or interface).</summary>
    private static bool Assignable(TypeReference to, TypeReference from) =>
        to.FullName == from.FullName || (!IsValueType(from) && !(from is ByReferenceType) && !(to is ByReferenceType) && IsSameOrBase(to, from));

    /// <summary>D's parameters and return type (null: void): System.Action / Func by their type arguments (no need to
    /// resolve the BCL), other delegates by their Invoke.</summary>
    private static bool DelegateShape(TypeReference delegateType, out List<TypeReference> parameters, out TypeReference returns)
    {
        parameters = new List<TypeReference>();
        returns = null;
        var element = delegateType.GetElementType();
        var arguments = (delegateType as GenericInstanceType)?.GenericArguments.ToList() ?? new List<TypeReference>();
        if (element.Namespace == "System" && (element.Name == "Action" || element.Name.StartsWith("Action`", StringComparison.Ordinal)))
        {
            parameters = arguments;
            return true;
        }
        if (element.Namespace == "System" && element.Name.StartsWith("Func`", StringComparison.Ordinal) && arguments.Count > 0)
        {
            parameters = arguments.Take(arguments.Count - 1).ToList();
            returns = arguments.Last();
            return true;
        }
        var invoke = Resolve(delegateType)?.Methods.FirstOrDefault(m => m.Name == "Invoke");
        if (invoke == null) return false;
        var map = GenericMap(delegateType);
        parameters = invoke.Parameters.Select(p => Substitute(p.ParameterType, map)).ToList();
        returns = invoke.ReturnType.FullName == "System.Void" ? null : Substitute(invoke.ReturnType, map);
        return true;
    }

    private static void Found(bool found, string shown, string problem, Report report)
    {
        if (found) report.Checked.Add(shown);
        else report.Problems.Add(problem);
    }

    private static readonly Instruction Merged = Instruction.Create(OpCodes.Nop); // different values meet (?:, ??)
    private static readonly Instruction Unknown = Instruction.Create(OpCodes.Nop); // an argument, the exception, an address…

    /// <summary>What a value is, from the instruction that produced it (see <see cref="Sources"/>). Knows the shapes
    /// lookup arguments are written in; anything else is an UnknownArg.</summary>
    private static Arg Value(Instruction i, Scan scan, int depth)
    {
        if (i == Merged) return new BranchArg();
        if (i == null || i == Unknown || depth > 16) return new UnknownArg();
        var op = i.OpCode;
        if (op == OpCodes.Ldstr) return new StringArg { Value = (string)i.Operand };
        if (op == OpCodes.Ldnull) return new NullArg();
        if (IntValue(i) is int n) return new IntArg { Value = n };
        if (op == OpCodes.Ldsfld && i.Operand is FieldReference f && f.DeclaringType.FullName == "System.Type" && f.Name == "EmptyTypes")
            return new TypesArg { Names = new string[0] };
        if (op == OpCodes.Newarr && i.Operand is TypeReference element && element.FullName == "System.Type")
            return TypeArray(i, scan, depth);
        if (op != OpCodes.Call || !(i.Operand is MethodReference called)) return new UnknownArg();
        if (called.DeclaringType.FullName == "System.Type" && called.Name == "GetTypeFromHandle")
        {
            var token = scan.Sources.TryGetValue(i, out var s) ? s[0] : null;
            if (token?.OpCode != OpCodes.Ldtoken || !(token.Operand is TypeReference type)) return new UnknownArg();
            return new TypeArg { Ref = type, Type = Resolve(type), Shown = $"typeof({Show(type)})" };
        }
        if (called.DeclaringType.FullName == "System.Array" && called.Name == "Empty" && called is GenericInstanceMethod g &&
            g.GenericArguments[0].FullName == "System.Type")
            return new TypesArg { Names = new string[0] };
        if (called.DeclaringType.FullName != AccessToolsType) return new UnknownArg();
        // A lookup inside another one: read it here (its problems are reported at its own call, so into a scratch report).
        var args = Arguments(i, scan, depth);
        if (args == null || args.Any(a => a is BranchArg)) return new UnknownArg();
        return AsArg(called, CheckLookup("", called, args, new Report(), scan), args);
    }

    /// <summary><c>new[] { typeof(A), … }</c>: the newarr's size, then its initializer right after it (dup; ldc index;
    /// ldtoken; GetTypeFromHandle; stelem.ref per element). Anything else using the array (another write, a call that gets
    /// it, ldelema) makes it unknown: only moving it (stloc) and passing it to AccessTools are allowed.</summary>
    private static Arg TypeArray(Instruction newarr, Scan scan, int depth)
    {
        if (!(Value(scan.Sources.TryGetValue(newarr, out var size) ? size[0] : null, scan, depth + 1) is IntArg count) || count.Value < 0)
            return new UnknownArg();
        var names = new string[count.Value];
        var initializer = new HashSet<Instruction>();
        for (var i = newarr.Next; i?.OpCode == OpCodes.Dup;)
        {
            var index = i.Next;
            var token = index?.Next;
            var getType = token?.Next;
            var store = getType?.Next;
            if (!(IntValue(index) is int k) || k < 0 || k >= names.Length || names[k] != null || token.OpCode != OpCodes.Ldtoken ||
                !(token.Operand is TypeReference element) || getType.OpCode != OpCodes.Call ||
                (getType.Operand as MethodReference)?.Name != "GetTypeFromHandle" || store?.OpCode != OpCodes.Stelem_Ref)
                return new UnknownArg();
            names[k] = element.FullName;
            initializer.Add(i);
            initializer.Add(store);
            i = store.Next;
        }
        foreach (var use in scan.Sources.Where(x => x.Value.Contains(newarr)).Select(x => x.Key))
            if (!initializer.Contains(use) && StoreIndex(use) == null && !IsAccessToolsCall(use, out _))
                return new UnknownArg();
        return new TypesArg { Names = names };
    }

    private static Arg AsArg(MethodReference called, Outcome outcome, Arg[] args)
    {
        if (outcome.Skipped != null) return new UnknownArg();
        if (called.Name == "TypeByName")
            return new TypeArg { Ref = outcome.Found as TypeDefinition, Type = outcome.Found as TypeDefinition, Shown = $"AccessTools.TypeByName(\"{(args[0] as StringArg)?.Value}\")" };
        if (called.Name == "Inner")
            return new TypeArg { Ref = outcome.Found as TypeDefinition, Type = outcome.Found as TypeDefinition, Shown = $"AccessTools.Inner({(args[0] as TypeArg)?.Shown}, \"{(args[1] as StringArg)?.Value}\")" };
        if (MemberLookups.Contains(called.Name))
            return new MemberArg { Field = outcome.Found as FieldDefinition, Method = outcome.Found as MethodDefinition };
        return new UnknownArg();
    }

    /// <summary>Follows the method's IL (stack and locals, through branches and handlers) and returns, for every call,
    /// newarr and stelem.ref, the instruction that produced each of its operands: <see cref="Merged"/> where different
    /// values meet, <see cref="Unknown"/> for arguments, exceptions and addresses. Null if the IL can't be followed.</summary>
    private static Dictionary<Instruction, Instruction[]> Sources(MethodDefinition method)
    {
        var body = method.Body;
        var sources = new Dictionary<Instruction, Instruction[]>();
        var states = new Dictionary<Instruction, (List<Instruction> Stack, Instruction[] Locals)>();
        var work = new Queue<Instruction>();
        var unknownLocals = Enumerable.Repeat(Unknown, body.Variables.Count).ToArray();
        // A local whose address is taken (a ref can write it later) or that a finally / fault block writes (that write
        // reaches the code after the try, which `leave` skips here) is never known.
        var opaque = new HashSet<int>(body.Instructions.Where(x => (x.OpCode.Code == Code.Ldloca || x.OpCode.Code == Code.Ldloca_S) && x.Operand is VariableDefinition)
            .Select(x => ((VariableDefinition)x.Operand).Index));
        foreach (var h in body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Finally || h.HandlerType == ExceptionHandlerType.Fault))
            for (var x = h.HandlerStart; x != null && x != h.HandlerEnd; x = x.Next)
                if (StoreIndex(x) is int written) opaque.Add(written);

        bool Flow(Instruction to, List<Instruction> stack, Instruction[] locals)
        {
            if (to == null) return true;
            if (!states.TryGetValue(to, out var old))
            {
                states[to] = (new List<Instruction>(stack), (Instruction[])locals.Clone());
                work.Enqueue(to);
                return true;
            }
            if (old.Stack.Count != stack.Count) return false;
            var changed = false;
            for (var k = 0; k < stack.Count; k++)
                if (old.Stack[k] != stack[k] && old.Stack[k] != Merged) { old.Stack[k] = Merged; changed = true; }
            for (var k = 0; k < locals.Length; k++)
                if (old.Locals[k] != locals[k] && old.Locals[k] != Merged) { old.Locals[k] = Merged; changed = true; }
            if (changed) work.Enqueue(to);
            return true;
        }

        if (body.Instructions.Count == 0 || !Flow(body.Instructions[0], new List<Instruction>(), unknownLocals)) return null;
        foreach (var h in body.ExceptionHandlers)
        {
            var thrown = h.HandlerType == ExceptionHandlerType.Catch || h.HandlerType == ExceptionHandlerType.Filter
                ? new List<Instruction> { Unknown } : new List<Instruction>();
            if (!Flow(h.HandlerStart, thrown, unknownLocals)) return null;
            if (h.FilterStart != null && !Flow(h.FilterStart, new List<Instruction> { Unknown }, unknownLocals)) return null;
        }
        for (var steps = 0; work.Count > 0; steps++)
        {
            if (steps > body.Instructions.Count * 50) return null;
            var i = work.Dequeue();
            var stack = new List<Instruction>(states[i].Stack);
            var locals = (Instruction[])states[i].Locals.Clone();
            var pops = Pops(i, method);
            var pushes = Pushes(i);
            if (pops < 0 || pushes < 0 || (pops != int.MaxValue && pops > stack.Count)) return null; // int.MaxValue: leave empties it
            var popped = pops == int.MaxValue ? stack.ToList() : stack.GetRange(stack.Count - pops, pops);
            stack.RemoveRange(stack.Count - popped.Count, popped.Count);
            var c = i.OpCode.Code;
            if (popped.Count > 0)
                sources[i] = sources.TryGetValue(i, out var seen) && seen.Length == popped.Count
                    ? seen.Zip(popped, (a, b) => a == b ? a : Merged).ToArray() : popped.ToArray();
            if (c == Code.Dup) stack.AddRange(new[] { popped[0], popped[0] });
            else if (StoreIndex(i) is int store && store < locals.Length) locals[store] = opaque.Contains(store) ? Unknown : popped[0];
            else if (LoadIndex(i) is int load && load < locals.Length) stack.Add(locals[load]);
            else if ((c == Code.Ldloca || c == Code.Ldloca_S) && i.Operand is VariableDefinition address)
            {
                locals[address.Index] = Unknown; // its address escapes: anything can write it
                stack.Add(Unknown);
            }
            else
                for (var k = 0; k < pushes; k++) stack.Add(i);
            var flow = i.OpCode.FlowControl;
            if (flow == FlowControl.Branch || flow == FlowControl.Cond_Branch)
                foreach (var target in i.Operand is Instruction[] cases ? cases : new[] { (Instruction)i.Operand })
                    if (!Flow(target, stack, locals)) return null;
            if (flow != FlowControl.Branch && flow != FlowControl.Return && flow != FlowControl.Throw && !Flow(i.Next, stack, locals))
                return null;
        }
        return sources;
    }

    /// <summary>How many values an instruction takes off the stack (int.MaxValue: all, -1: not followed).</summary>
    private static int Pops(Instruction i, MethodDefinition method)
    {
        switch (i.OpCode.StackBehaviourPop)
        {
            case StackBehaviour.Pop0: return 0;
            case StackBehaviour.Pop1: case StackBehaviour.Popi: case StackBehaviour.Popref: return 1;
            case StackBehaviour.Pop1_pop1: case StackBehaviour.Popi_pop1: case StackBehaviour.Popi_popi: case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4: case StackBehaviour.Popi_popr8: case StackBehaviour.Popref_pop1: case StackBehaviour.Popref_popi:
                return 2;
            case StackBehaviour.Popi_popi_popi: case StackBehaviour.Popref_popi_popi: case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4: case StackBehaviour.Popref_popi_popr8: case StackBehaviour.Popref_popi_popref:
                return 3;
            case StackBehaviour.PopAll: return int.MaxValue;
            case StackBehaviour.Varpop:
                if (i.OpCode.Code == Code.Ret) return IsVoid(method.ReturnType) ? 0 : 1;
                if (!(i.Operand is MethodReference m) || i.OpCode.Code == Code.Calli) return -1;
                return m.Parameters.Count + (m.HasThis && !m.ExplicitThis && i.OpCode.Code != Code.Newobj ? 1 : 0);
            default: return -1;
        }
    }

    private static int Pushes(Instruction i)
    {
        switch (i.OpCode.StackBehaviourPush)
        {
            case StackBehaviour.Push0: return 0;
            case StackBehaviour.Push1: case StackBehaviour.Pushi: case StackBehaviour.Pushi8: case StackBehaviour.Pushr4:
            case StackBehaviour.Pushr8: case StackBehaviour.Pushref:
                return 1;
            case StackBehaviour.Push1_push1: return 2;
            case StackBehaviour.Varpush:
                if (!(i.Operand is MethodReference m) || i.OpCode.Code == Code.Calli) return -1;
                return i.OpCode.Code == Code.Newobj || !IsVoid(m.ReturnType) ? 1 : 0;
            default: return -1;
        }
    }

    /// <summary>void, also with modifiers (an init accessor returns void modreq(IsExternalInit)).</summary>
    private static bool IsVoid(TypeReference type)
    {
        while (type is IModifierType modified) type = modified.ElementType;
        return type.MetadataType == MetadataType.Void;
    }

    private static int? StoreIndex(Instruction i)
    {
        var c = i.OpCode.Code;
        if (c >= Code.Stloc_0 && c <= Code.Stloc_3) return c - Code.Stloc_0;
        return c == Code.Stloc || c == Code.Stloc_S ? (i.Operand as VariableDefinition)?.Index : null;
    }

    private static int? LoadIndex(Instruction i)
    {
        var c = i.OpCode.Code;
        if (c >= Code.Ldloc_0 && c <= Code.Ldloc_3) return c - Code.Ldloc_0;
        return c == Code.Ldloc || c == Code.Ldloc_S ? (i.Operand as VariableDefinition)?.Index : null;
    }

    private static int? IntValue(Instruction i)
    {
        if (i.OpCode == OpCodes.Ldc_I4_M1) return -1;
        if (i.OpCode.Code >= Code.Ldc_I4_0 && i.OpCode.Code <= Code.Ldc_I4_8) return i.OpCode.Code - Code.Ldc_I4_0;
        if (i.OpCode == OpCodes.Ldc_I4_S) return (sbyte)i.Operand;
        if (i.OpCode == OpCodes.Ldc_I4) return (int)i.Operand;
        return null;
    }

    /// <summary>AccessTools.TypeByName: by full name (nested types written A+B), then by name. HarmonyX searches the
    /// loaded assemblies in load order; here every DLL in name order, so a short name that matches several types is
    /// reported (<paramref name="several"/>) rather than guessed.</summary>
    private static TypeDefinition TypeByName(string name, Lazy<List<TypeDefinition>> types, out string several)
    {
        several = null;
        var full = types.Value.FirstOrDefault(t => t.FullName.Replace('/', '+') == name);
        if (full != null) return full;
        var named = types.Value.Where(t => t.Name == name).ToList();
        if (named.Count > 1) several = string.Join(", ", named.Select(Show));
        return named.FirstOrDefault();
    }

    /// <summary>AccessTools.Inner: a nested type of the type or its base types.</summary>
    private static TypeDefinition Inner(TypeDefinition type, string name)
    {
        for (var t = type; t != null; t = Resolve(t.BaseType))
        {
            var nested = t.NestedTypes.FirstOrDefault(n => n.Name == name);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>AccessTools.Method / DeclaredMethod. Without parameter types: Method takes the overload without
    /// parameters when there are several, DeclaredMethod throws (<paramref name="overloaded"/>).</summary>
    private static MethodDefinition FindMethod(TypeDefinition type, string name, string[] parameters, bool declaredOnly, out bool overloaded)
    {
        overloaded = false;
        for (var t = type; t != null; t = declaredOnly ? null : Resolve(t.BaseType))
        {
            // t.GetMethod(name, all…) sees its own methods and, for Method, the non-private instance ones it inherits.
            var visible = declaredOnly ? t.Methods.Where(m => m.Name == name).ToList() : Visible(t, name);
            if (parameters != null)
            {
                var exact = visible.FirstOrDefault(m => ParameterTypes(m).SequenceEqual(parameters));
                if (exact != null) return exact;
                continue;
            }
            if (visible.Count == 0) continue;
            if (visible.Count == 1) return visible[0];
            var parameterless = declaredOnly ? null : visible.FirstOrDefault(m => m.Parameters.Count == 0);
            if (parameterless != null) return parameterless;
            overloaded = true;
            return null;
        }
        return null;
    }

    /// <summary>AccessTools.ValidateFieldType: F is the field's type, an enum field's underlying type, or for a reference
    /// type object or a base type or interface of it (arrays: object or System.Array).</summary>
    private static bool FieldTypeFits(TypeReference wanted, TypeReference fieldType)
    {
        if (wanted.FullName == fieldType.FullName) return true;
        var definition = fieldType is ArrayType ? null : Resolve(fieldType);
        if (definition?.IsEnum == true)
            return definition.Fields.FirstOrDefault(f => f.Name == "value__")?.FieldType.FullName == wanted.FullName;
        if (IsValueType(fieldType)) return false;
        return IsSameOrBase(wanted, fieldType);
    }

    /// <summary>What <c>t.GetMethod(name, all)</c> sees: the type's own methods and the non-private instance ones it inherits.</summary>
    private static List<MethodDefinition> Visible(TypeDefinition type, string name)
    {
        var seen = type.Methods.Where(m => m.Name == name).ToList();
        for (var b = Resolve(type.BaseType); b != null; b = Resolve(b.BaseType))
            foreach (var m in b.Methods.Where(m => m.Name == name && !m.IsPrivate && !m.IsStatic))
                if (!seen.Any(s => ParameterTypes(s).SequenceEqual(ParameterTypes(m))))
                    seen.Add(m); // an override or a hiding method counts once
        return seen;
    }

    /// <summary>AccessTools.Field searches the base types too (FindIncludingBaseTypes); DeclaredField doesn't.</summary>
    private static FieldDefinition FindField(TypeDefinition type, string name, bool declaredOnly)
    {
        for (var t = type; t != null; t = declaredOnly ? null : Resolve(t.BaseType))
        {
            var field = t.Fields.FirstOrDefault(f => f.Name == name);
            if (field != null) return field;
        }
        return null;
    }

    // ---- helpers

    private static TypeDefinition Resolve(TypeReference type)
    {
        if (type == null) return null;
        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    private static bool IsValueType(TypeReference type) =>
        type.IsPrimitive || type.IsValueType || (!(type is ArrayType) && !(type is ByReferenceType) && Resolve(type)?.IsValueType == true);

    /// <summary>Whether a value of <paramref name="actual"/> fits a hook parameter of <paramref name="wanted"/> (Harmony's
    /// IsAssignableFrom): the same type, a base type, an interface it implements (generic arguments filled in), or object
    /// (Harmony boxes). An array fits only itself, object and System.Array.</summary>
    private static bool IsSameOrBase(TypeReference wanted, TypeReference actual)
    {
        if (wanted.FullName == "System.Object") return true;
        if (actual is ArrayType) return wanted.FullName == actual.FullName || wanted.FullName == "System.Array";
        return Supertypes(actual, 0).Any(t => t.FullName == wanted.FullName);
    }

    /// <summary>The type, its base types and its interfaces, with generic arguments substituted (FakeSeq&lt;string&gt;
    /// is an IFakeSeq&lt;string&gt;, List&lt;int&gt; an IEnumerable&lt;int&gt;).</summary>
    private static IEnumerable<TypeReference> Supertypes(TypeReference type, int depth)
    {
        if (type == null || depth > 32) yield break;
        yield return type;
        var definition = Resolve(type);
        if (definition == null) yield break;
        var map = GenericMap(type);
        foreach (var i in definition.Interfaces)
        foreach (var t in Supertypes(Substitute(i.InterfaceType, map), depth + 1))
            yield return t;
        foreach (var t in Supertypes(Substitute(definition.BaseType, map), depth + 1))
            yield return t;
    }

    /// <summary>A generic instance's type parameters by name: List&lt;int&gt; → { T: int }.</summary>
    private static Dictionary<string, TypeReference> GenericMap(TypeReference type)
    {
        if (!(type is GenericInstanceType g) || !(Resolve(type) is TypeDefinition definition)) return null;
        var map = new Dictionary<string, TypeReference>();
        for (var k = 0; k < definition.GenericParameters.Count && k < g.GenericArguments.Count; k++)
            map[definition.GenericParameters[k].Name] = g.GenericArguments[k];
        return map;
    }

    private static TypeReference Substitute(TypeReference type, Dictionary<string, TypeReference> map)
    {
        if (type == null || map == null) return type;
        if (type is GenericParameter p && p.Type == GenericParameterType.Type && map.TryGetValue(p.Name, out var argument)) return argument;
        if (!(type is GenericInstanceType g)) return type;
        var copy = new GenericInstanceType(g.ElementType);
        foreach (var a in g.GenericArguments) copy.GenericArguments.Add(Substitute(a, map));
        return copy;
    }

    private static TypeReference ElementType(TypeReference type) => type is ByReferenceType byRef ? byRef.ElementType : type;

    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType != null) type = type.DeclaringType;
        return type;
    }

    /// <summary>Compared as names, never resolved: a parameter type may live in a DLL the check doesn't need.</summary>
    private static IEnumerable<string> ParameterTypes(MethodDefinition m) => m.Parameters.Select(p => p.ParameterType.FullName);

    private static string Show(TypeReference type) => type.FullName.Replace('/', '.');
}
