using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Raising game events (TPS.Core.GameEvents) from managed code.
///
/// The generated interop wrapper for the generic `GameEvents.Raise&lt;TEvent&gt;([In] ref TEvent e)`
/// is broken for struct events: it passes a pointer to the *boxed object pointer* instead of a
/// pointer to the struct data (compare the non-generic ExpeditionManager.OnRewardClaimed wrapper,
/// which correctly passes il2cpp_object_unbox(e)). The game then reads an object header as the
/// event's fields and crashes while processing the event. So we invoke the inflated method
/// ourselves with the unboxed pointer, reusing the wrapper's own method-pointer cache.
/// </summary>
internal static class GameEventsUtil
{
    public static unsafe void Raise<TEvent>(TEvent e) where TEvent : Il2CppSystem.ValueType, new()
    {
        var events = TPS.Core.GameEvents.Instance ?? throw new InvalidOperationException("GameEvents not available");
        IntPtr method = InflatedMethod<TEvent>("MethodInfoStoreGeneric_Raise_Public_Void_byref_TEvent_0`1");

        IntPtr boxed = IL2CPP.Il2CppObjectBaseToPtrNotNull(e);
        IntPtr* args = stackalloc IntPtr[1];
        args[0] = IL2CPP.il2cpp_object_unbox(boxed); // [In] ref TEvent => pointer to the struct data
        IntPtr exc = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(method, IL2CPP.Il2CppObjectBaseToPtrNotNull(events), (void**)args, ref exc);
        Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
    }

    /// <summary>The wrapper's per-instantiation cache class (static field `Pointer`) for a generic method.</summary>
    private static IntPtr InflatedMethod<TEvent>(string storeTypeName)
    {
        var store = typeof(TPS.Core.GameEvents).GetNestedType(storeTypeName, BindingFlags.NonPublic | BindingFlags.Public)
                    ?? throw new MissingMemberException(nameof(TPS.Core.GameEvents), storeTypeName);
        var field = store.MakeGenericType(typeof(TEvent)).GetField("Pointer", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    ?? throw new MissingFieldException(storeTypeName, "Pointer");
        var ptr = (IntPtr)field.GetValue(null);
        if (ptr == IntPtr.Zero) throw new InvalidOperationException($"{storeTypeName}<{typeof(TEvent).Name}> did not resolve");
        return ptr;
    }
}
