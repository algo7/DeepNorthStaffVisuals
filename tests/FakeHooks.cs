using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

// Hooks broken on purpose, for HookCheckTests. Never patched or run: the hook check reads
// this assembly as a file and must report exactly the broken ones (each says why in a comment). They hook the fake
// classes below, not the game, so a game update can't make these tests fail (the one game example is in
// GameExampleHooks at the end).
namespace FakeHooks
{
    [HarmonyPatch]
    internal static class Broken
    {
        // Broken: no such method.
        [HarmonyPatch(typeof(FakeGame), "NoSuchMethod")]
        [HarmonyPostfix]
        private static void Missing() { }

        // Broken: Damage() and Damage(int, float), and no parameter types given.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Damage))]
        [HarmonyPostfix]
        private static void Ambiguous() { }

        // Broken: no Damage(string).
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Damage), typeof(string))]
        [HarmonyPostfix]
        private static void WrongArguments() { }

        // Broken: 1 byte of IL, not virtual, called directly: Mono inlines it there, and those calls skip the postfix.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Tiny))]
        [HarmonyPostfix]
        private static void Tiny() { }

        // Broken: an interface method is virtual but final: called on the class directly, Mono can inline it too.
        [HarmonyPatch(typeof(FakeImpl), nameof(FakeImpl.Bar))]
        [HarmonyPostfix]
        private static void FinalTiny() { }

        // Broken: AggressiveInlining lifts Mono's size limit.
        [HarmonyPatch(typeof(FakeAggressive), nameof(FakeAggressive.Big))]
        [HarmonyPostfix]
        private static void Aggressive() { }

        // Fine: small, but really virtual: not inlined.
        [HarmonyPatch(typeof(FakeVirtual), nameof(FakeVirtual.Baz))]
        [HarmonyPostfix]
        private static void Virtual() { }

        // Broken: virtual, but also called with `call` (base.Qux()): that call can be inlined.
        [HarmonyPatch(typeof(FakeVirtualBase), nameof(FakeVirtualBase.Qux))]
        [HarmonyPostfix]
        private static void BaseCall() { }

        // Fine: a reverse patch copies the original; inlining at the callers doesn't matter.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Tiny))]
        [HarmonyReversePatch]
        private static void ReverseTiny(FakeGame instance) { }

        // Broken: a form the check doesn't read.
        [HarmonyPatch(typeof(FakeGame), MethodType.Constructor)]
        [HarmonyPostfix]
        private static void Unsupported() { }

        // Broken: no [HarmonyPrefix] / [HarmonyPostfix] and not named Prefix / Postfix: Harmony never runs it.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Block))]
        private static void Helper() { }

        // One method, two targets: the first is fine, the second broken.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Block))]
        [HarmonyPatch(typeof(FakeGame), "NoSuchTarget")]
        [HarmonyPrefix]
        private static void Two() { }

        // Fine: overloaded, but the parameter types are given.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Damage), typeof(int), typeof(float))]
        [HarmonyPostfix]
        private static void Overload() { }

        private static void Names(string suffix)
        {
            AccessTools.Field(typeof(FakeGame), "m_noSuchField"); // broken
            AccessTools.Method(typeof(FakeGame), "NoSuchMethod"); // broken
            AccessTools.Method(typeof(FakeDerived), "Foo"); // broken: Foo(int) here, Foo(string) inherited, none without parameters
            AccessTools.Field(typeof(FakeGame), "m_own"); // fine
            AccessTools.Field(typeof(FakeGame), "m_inherited"); // fine: declared on the base type
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Block)); // fine
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Damage)); // fine: AccessTools takes Damage()
            AccessTools.Field(typeof(FakeGame), "m_" + suffix); // skipped: the name is built at runtime
            AccessTools.Property(typeof(FakeGame), "Anything"); // skipped: a lookup the check doesn't read
        }

        // The other lookups the sibling mods use. Each fine one touches a member nothing else here does, so the test sees
        // that this exact case was read.
        private static void MoreNames(FieldInfo field, bool flag)
        {
            AccessTools.FieldRefAccess<FakeGame, int>("m_refOnly"); // fine
            AccessTools.FieldRefAccess<FakeGame, int>("m_noSuchField"); // broken: no such field
            AccessTools.FieldRefAccess<FakeGame, string>("m_own"); // broken: the field is an int
            AccessTools.FieldRefAccess<FakeGame, int>("m_kind"); // broken: an enum backed by a byte
            AccessTools.FieldRefAccess<FakeGame, int>("s_count"); // broken: a static field
            AccessTools.FieldRefAccess<FakeGame, byte>("m_kind"); // fine: the enum's underlying type
            AccessTools.FieldRefAccess<FakeGame, IFakeSeq<string>>("m_seq"); // fine: an interface the field's type implements
            AccessTools.FieldRefAccess<FakeGame, IFakeSeq<int>>("m_seq"); // broken: FakeSeq<string> isn't an IFakeSeq<int>
            AccessTools.FieldRefAccess<FakeGame, System.Array>("m_arr"); // fine
            AccessTools.FieldRefAccess<FakeGame, FakeGameBase>("m_arr"); // broken: an array isn't its element type
            AccessTools.FieldRefAccess<int>(typeof(FakeGame), "m_inheritedRef"); // fine: on the base type
            AccessTools.FieldRefAccess<FakeGame, int>(new FakeGame(), "m_instOnly"); // fine: the instance form
            AccessTools.FieldRefAccess<FakeStruct, int>("m_x"); // broken: T can't be a struct
            AccessTools.FieldRefAccess<int>(typeof(FakeStruct), "m_x"); // broken: an instance field of a struct
            AccessTools.FieldRefAccess<int>(typeof(FakeStruct), "s_y"); // fine: a static field of a struct
            AccessTools.StaticFieldRefAccess<int>(typeof(FakeGame), "s_count"); // fine
            AccessTools.StaticFieldRefAccess<FakeGame, int>("m_own"); // broken: not static
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Power), new[] { typeof(int) }); // fine
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Damage), new[] { typeof(string) }); // broken: no Damage(string)
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Block), System.Type.EmptyTypes); // fine
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Load), new[] { typeof(byte[]), typeof(List<int>) }); // fine: array and generic types
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Gen), null, new[] { typeof(int) }); // fine: generic, one type parameter
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Use), null, new[] { typeof(int) }); // broken: Use isn't generic
            AccessTools.Method(AccessTools.TypeByName("FakeHooks.FakeOther"), nameof(FakeOther.Only)); // fine
            AccessTools.TypeByName("FakeHooks.NoSuchType"); // broken
            AccessTools.TypeByName("Twin"); // broken: two types are named Twin
            AccessTools.Field(AccessTools.Inner(typeof(FakeGame), "Nested"), "m_x"); // fine
            AccessTools.Inner(typeof(FakeGame), "NoSuchNested"); // broken
            AccessTools.Field(flag ? typeof(FakeGame) : typeof(FakeOther), "m_own"); // skipped: which type depends on a branch
            AccessTools.Field(AccessTools.TypeByName("FakeHooks.FakeOther") ?? typeof(FakeGame), "m_own"); // skipped: a branch (??)
            AccessTools.Field(typeof(FakeGame[]), "m_own"); // skipped: an array type isn't read
            AccessTools.StaticFieldRefAccess<int>(field); // skipped: a FieldInfo the check can't trace
        }

        // FieldInfo / MethodInfo from a lookup, passed on (directly or through a local set once), as the sibling mods do.
        private static void PassedOn()
        {
            AccessTools.MethodDelegate<System.Action<FakeGame>>(AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Deleg))); // fine
            AccessTools.MethodDelegate<System.Func<FakeGame, int>>(AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Deleg))); // broken: Deleg returns nothing
            var deleg2 = AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Deleg2));
            if (deleg2 == null) throw new System.MissingMethodException();
            AccessTools.MethodDelegate<System.Action<FakeGame>>(deleg2); // fine: through a local
            var use = AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Use));
            AccessTools.MethodDelegate<System.Action<FakeGame>>(use); // broken: Use takes a float
            AccessTools.MethodDelegate<System.Func<int>>(AccessTools.Method(typeof(FakeGame), nameof(FakeGame.StaticCount))); // fine: static
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(FakeGame), "m_staticOnly")); // fine
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(FakeGame), "m_own")); // broken: not static
            var own = AccessTools.Field(typeof(FakeGame), "m_own");
            AccessTools.FieldRefAccess<FakeGame, string>(own); // broken: the field is an int
        }

        // Values the check can't vouch for: written after the initializer, in a finally, through an address, generic types.
        private static void Opaque(bool flag)
        {
            var later = new[] { typeof(int) };
            later[0] = typeof(string);
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Power), later); // skipped: the array is written again
            var types = new[] { typeof(int) };
            AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Power2), types); // fine: an initializer, through a local
            var t = typeof(FakeGame);
            try { } finally { if (flag) t = typeof(FakeOther); }
            AccessTools.Field(t, "m_own"); // skipped: a finally block writes t
            var u = typeof(FakeGame);
            Retype(ref u);
            AccessTools.Field(u, "m_own"); // skipped: its address was passed on
            AccessTools.Field(typeof(FakeSeq<int>), "m_val"); // skipped: a generic instance
            AccessTools.FieldRefAccess<FakeSeq<int>, object>("m_val"); // skipped: a generic instance
            AccessTools.StaticFieldRefAccess<int>(typeof(FakeGame), "c_const"); // broken: a constant has no address
            AccessTools.FieldRefAccess<FakeGame, int>(new FakeGame(), AccessTools.Field(typeof(FakeGame), "s_count")); // broken: static
            AccessTools.MethodDelegate<System.Action<IFake>>(AccessTools.Method(typeof(IFake), nameof(IFake.Bar)), null, false); // broken
            AccessTools.MethodDelegate<System.Action<FakeGame>>(AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Gen))); // skipped: generic
        }

        private static void Retype(ref System.Type type) => type = typeof(FakeOther);

        // Harmony APIs that look things up or patch outside AccessTools and [HarmonyPatch]: listed, never silently passed.
        private static void Escapes(Harmony harmony)
        {
            Traverse.Create(new FakeGame()).Field("m_own"); // skipped twice: Create and Field
            CodeInstruction.Call(typeof(FakeGame), nameof(FakeGame.Block)); // skipped
            new HarmonyMethod(typeof(FakeGame), nameof(FakeGame.Block)); // skipped
            harmony.Patch(AccessTools.Method(typeof(FakeGame), nameof(FakeGame.Block2))); // skipped (its Method is checked)
            harmony.PatchAll(); // skipped: the check assumes PatchAll(typeof(X))
        }

        // Lookups inside try / catch, as the sibling mods guard theirs.
        private static bool Guarded()
        {
            try
            {
                AccessTools.FieldRefAccess<FakeGame, int>("m_guarded"); // fine
                AccessTools.FieldRefAccess<FakeGame, int>("m_noSuchGuarded"); // broken
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // Direct calls, as the game would make them (Mono inlines at direct calls).
        private static void Calls()
        {
            new FakeGame().Tiny();
            new FakeImpl().Bar();
            FakeAggressive.Big();
            new FakeVirtual().Baz();
        }
    }

    // Harmony binds hook parameters by name (or a special name): a game rename breaks the hook.
    [HarmonyPatch]
    internal static class Parameters
    {
        // Broken: Use(float v) has no "wrongName".
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void WrongName(ref float wrongName) { }

        // Broken: v is a float.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void WrongType(int v) { }

        // Broken: the method returns nothing.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPostfix]
        private static void ResultOfVoid(object __result) { }

        // Broken: Have returns a bool.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Have))]
        [HarmonyPostfix]
        private static void ResultWrongType(int __result) { }

        // Broken: no such field.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void MissingField(object ___m_noSuchField) { }

        // Broken: the field is an int.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void FieldWrongType(string ___m_inherited) { }

        // Broken: Use has one parameter (__0).
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void BadIndex(float __1) { }

        // Broken: not a name HarmonyX knows.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void UnknownSpecial(object __foo) { }

        // Broken: __args must be object[].
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void ArgsWrongType(object __args) { }

        // Broken: __exception exists only for finalizers.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPostfix]
        private static void ExceptionInPostfix(System.Exception __exception) { }

        // Broken: a prefix returns void or bool.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static int PrefixReturnsInt(float v) => 0;

        // Broken: a postfix that returns a value must take it first (pass-through).
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Power))]
        [HarmonyPostfix]
        private static float PassThroughWrong(int quality) => 0;

        // Broken: renamed parameters aren't read.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void Renamed([HarmonyArgument("v")] float cost) { }

        // Fine: every kind of parameter, right.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyPrefix]
        private static void Fine(FakeGame __instance, ref float v, int ___m_inherited, float __0, object __originalMethod,
            ref bool __runOriginal, object[] __args, out int __state) => __state = 0;

        // Fine: a base type for __instance, object for a bool __result, a parameter by name.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Have))]
        [HarmonyPostfix]
        private static void FineLoose(FakeGameBase __instance, object __result, float amount) { }

        // Fine: a pass-through postfix (its first parameter is the result, not one of the target's).
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Power))]
        [HarmonyPostfix]
        private static float PassThrough(float value, int quality) => value;

        // Fine: an interface the result implements (FakeSeq<string> is an IFakeSeq<string>).
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Items))]
        [HarmonyPostfix]
        private static void InterfaceResult(IFakeSeq<string> __result) { }

        // Fine: __exception in a finalizer.
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Use))]
        [HarmonyFinalizer]
        private static void Finalizer(System.Exception __exception) { }
    }

    // The type on the class, the name on the method.
    [HarmonyPatch(typeof(FakeGame))]
    internal static class ClassLevel
    {
        [HarmonyPatch("NoSuchMethodEither")] // broken
        [HarmonyPrefix]
        private static void Prefix() { }
    }

    [HarmonyPatch(typeof(FakeGame))]
    internal static class GoodClassLevel
    {
        [HarmonyPatch(nameof(FakeGame.Block))] // fine
        private static void Prefix() { }
    }

    // With the type on the class, every attribute naming a method is a target of its own: NoSuchA is broken.
    [HarmonyPatch(typeof(FakeGame))]
    internal static class ClassLevelTwo
    {
        [HarmonyPatch("NoSuchA")]
        [HarmonyPatch(nameof(FakeGame.Block))]
        private static void Prefix() { }
    }

    // No [HarmonyPatch] on the class, but harmony.PatchAll(typeof(X)) (how the plugin patches) still patches it.
    internal static class Unannotated
    {
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Block))] // fine
        [HarmonyPrefix]
        private static void Block() { }

        [HarmonyPostfix] // broken: no target, so Harmony rejects the class
        private static void Stray() { }
    }

    // Broken: the target is picked at runtime.
    [HarmonyPatch]
    internal static class Dynamic
    {
        [HarmonyTargetMethod]
        private static MethodBase Pick() => null;

        [HarmonyPostfix]
        private static void After() { }
    }

    // Broken: no [HarmonyPatch], but PatchAll(typeof(X)) still patches it, on whatever TargetMethod returns.
    internal static class UnannotatedDynamic
    {
        private static MethodBase TargetMethod() => null;

        private static void Postfix() { }
    }

    // Broken: Harmony requires static hooks.
    [HarmonyPatch]
    internal class NotStatic
    {
        [HarmonyPatch(typeof(FakeGame), nameof(FakeGame.Block))]
        [HarmonyPrefix]
        private void Prefix() { }
    }

    // ---- What the fakes hook, standing in for game classes. Only the methods Broken.Calls calls are called directly.

    internal class FakeGameBase
    {
        protected int m_inherited;
        protected int m_inheritedRef;
    }

    internal enum FakeKind : byte { One }

    internal class FakeGame : FakeGameBase
    {
        private static int s_count;
        private static int m_staticOnly;
        private int m_own;
        private int m_refOnly;
        private int m_instOnly;
        private int m_guarded;
        private const int c_const = 1;
        private FakeKind m_kind;
        private FakeSeq<string> m_seq;
        private FakeGame[] m_arr;

        internal class Nested
        {
            public int m_x;
        }

        public void Damage() { }

        public void Damage(int quality, float worldLevel) { }

        public void Tiny() { }

        public void Block() { }

        public void Use(float v) { }

        public bool Have(float amount) => amount > m_own;

        public float Power(int quality) => quality;

        public FakeSeq<string> Items() => null;

        public void Load(byte[] data, List<int> sizes) { }

        public void Gen<TX>() { }

        public void Deleg() { }

        public void Deleg2() { }

        public float Power2(int quality) => quality;

        public void Block2() { }

        public static int StaticCount() => s_count;
    }

    internal class FakeOther
    {
        public void Only() { }
    }

    internal struct FakeStruct
    {
        public int m_x;
        public static int s_y;
    }

    internal class Twin { }

    internal interface IFakeSeq<T> { }

    internal class FakeSeq<T> : IFakeSeq<T>
    {
        public T m_val;
    }

    internal interface IFake
    {
        void Bar();
    }

    internal class FakeImpl : IFake
    {
        public void Bar() { }
    }

    internal static class FakeAggressive
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Big()
        {
            System.Console.WriteLine("a");
            System.Console.WriteLine("b");
            System.Console.WriteLine("c");
            System.Console.WriteLine("d");
        }
    }

    internal class FakeVirtual
    {
        public virtual void Baz() { }
    }

    internal class FakeVirtualBase
    {
        public virtual void Qux() { }
    }

    internal class FakeVirtualChild : FakeVirtualBase
    {
        public override void Qux() => base.Qux();
    }

    internal class FakeBase
    {
        public void Foo(string s) { }
    }

    internal class FakeDerived : FakeBase
    {
        public void Foo(int i) { }
    }
}

// A second type named Twin, for TypeByName("Twin") (its own namespace: not one of the fakes the tests check).
namespace FakeHooksTwin
{
    internal class Twin { }
}

// The one fake that needs the game: a tiny method in one game DLL called from another (Mono inlines across
// assemblies), for HookCheck_SeesCallsFromOtherGameDlls.
namespace GameExampleHooks
{
    [HarmonyPatch]
    internal static class GameExample
    {
        // GuiBar.GetSmoothValue: 7 bytes of IL in assembly_guiutils, called from assembly_valheim.
        [HarmonyPatch(typeof(GuiBar), "GetSmoothValue")]
        [HarmonyPostfix]
        private static void OtherModule() { }
    }
}
