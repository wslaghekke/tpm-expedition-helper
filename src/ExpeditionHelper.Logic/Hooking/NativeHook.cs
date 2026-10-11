using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Runtime.InteropTypes;

namespace ExpeditionHelper.Logic.Hooking;

/// <summary>
/// Raw native detour on an il2cpp method, with an explicit unmanaged signature.
///
/// We don't use Harmony for game methods: its automatic argument conversion (Il2CppInterop
/// HarmonySupport) crashed on struct returns and `ref` struct arguments. Here every argument
/// arrives exactly as the native code passes it (object pointers as IntPtr, `ref` as a pointer
/// to the data) and the hook decides what to read.
///
/// The target is found by name through il2cpp metadata (via the generated wrapper), so hooks
/// survive game updates that move code; a changed signature is detected at install time.
///
/// il2cpp calling convention (x64): instance methods get `this` first, every method gets a
/// trailing `MethodInfo*`. So `void Foo.Bar(Baz b)` → `delegate void Bar(IntPtr self, IntPtr b, IntPtr method)`.
/// Detours must never let an exception escape into native code: wrap the body in try/catch.
/// </summary>
internal sealed class NativeHook<TDelegate> : IDisposable where TDelegate : Delegate
{
    private INativeDetour _detour;
    private readonly TDelegate _detourDelegate; // keep alive: native code holds a pointer to its thunk

    public MethodBase Target { get; }
    public TDelegate Original { get; }

    private NativeHook(MethodBase target, TDelegate detour)
    {
        Target = target;
        _detourDelegate = detour;
        _detour = INativeDetour.CreateAndApply(NativeCodeCheck.NativeCode(target), detour, out TDelegate original);
        Original = original;
    }

    /// <summary>Install a detour, or return null (with a logged reason) when it isn't safe.</summary>
    public static NativeHook<TDelegate> TryCreate(MethodBase target, TDelegate detour)
    {
        string name = $"{target?.DeclaringType?.Name}.{target?.Name}";
        if (target == null)
        {
            Entry.Host.Log.LogError("[hook] target method not found (renamed in a game update?)");
            return null;
        }
        string why = SignatureMismatch(target) ?? NativeCodeCheck.WhyUnsafe(target);
        if (why != null)
        {
            Entry.Host.Log.LogError($"[hook] not hooking {name}: {why}");
            return null;
        }
        var hook = new NativeHook<TDelegate>(target, detour);
        Hooks.Register(hook);
        Entry.Host.Log.LogInfo($"[hook] hooked {name}");
        return hook;
    }

    public void Dispose()
    {
        _detour?.Dispose(); // restores the original bytes
        _detour = null;
    }

    /// <summary>Compares the il2cpp method's parameters with the native delegate's.</summary>
    private static string SignatureMismatch(MethodBase target)
    {
        var invoke = typeof(TDelegate).GetMethod("Invoke")!;
        var expected = new List<Type>();
        if (!target.IsStatic) expected.Add(typeof(IntPtr));
        foreach (var p in target.GetParameters())
        {
            var t = NativeType(p.ParameterType);
            if (t == null) return $"parameter '{p.Name}' ({p.ParameterType.Name}) has no supported native form";
            expected.Add(t);
        }
        expected.Add(typeof(IntPtr)); // MethodInfo*

        var actual = invoke.GetParameters().Select(p => p.ParameterType).ToList();
        if (!expected.SequenceEqual(actual))
            return $"native signature is ({string.Join(", ", expected.Select(t => t.Name))}) but the hook delegate takes ({string.Join(", ", actual.Select(t => t.Name))}) — game method changed?";

        var ret = target is MethodInfo mi ? mi.ReturnType : typeof(void);
        var expectedRet = ret == typeof(void) ? typeof(void) : NativeType(ret);
        if (expectedRet == null || (ret != typeof(void) && ret.IsValueType && !ret.IsPrimitive && !ret.IsEnum)) // typeof(void).IsValueType is true
            return $"return type {ret.Name} isn't supported (struct returns use a hidden buffer)";
        if (invoke.ReturnType != expectedRet)
            return $"returns {expectedRet.Name} natively but the hook delegate returns {invoke.ReturnType.Name}";
        return null;
    }

    /// <summary>How a managed interop type is passed in the il2cpp native ABI (x64), or null if unsupported.</summary>
    private static Type NativeType(Type t)
    {
        if (t.IsByRef || t.IsPointer || t == typeof(string)) return typeof(IntPtr);
        if (typeof(Il2CppSystem.ValueType).IsAssignableFrom(t)) return null; // non-blittable struct by value
        if (typeof(Il2CppObjectBase).IsAssignableFrom(t) || t.IsInterface) return typeof(IntPtr);
        if (t.IsEnum) return Enum.GetUnderlyingType(t);
        if (t.IsPrimitive || t == typeof(IntPtr)) return t;
        if (t.IsValueType)
        {
            int size = Marshal.SizeOf(t);
            return size is 1 or 2 or 4 or 8 ? t : typeof(IntPtr); // larger structs are passed by pointer to a copy
        }
        return null;
    }
}

/// <summary>All hooks installed by this logic version; removed on unload so hot reload starts clean.</summary>
internal static class Hooks
{
    private static readonly List<IDisposable> Installed = new();

    public static void Register(IDisposable hook) => Installed.Add(hook);

    public static void DisposeAll()
    {
        foreach (var h in Installed)
        {
            try { h.Dispose(); }
            catch (Exception e) { Entry.Host.Log.LogError($"[hook] dispose failed: {e}"); }
        }
        Installed.Clear();
    }
}
