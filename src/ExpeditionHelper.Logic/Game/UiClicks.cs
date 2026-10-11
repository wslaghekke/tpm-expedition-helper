using System;
using System.Reflection;
using System.Runtime.InteropServices;
using ExpeditionHelper.Logic.Hooking;
using TPS.Core.UI;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Clicks on any of the game's UI elements (UINavigationFocus: buttons, tabs, ...), so cloned
/// widgets can react without handing delegates to the game (not possible under Denuvo).
///
/// Hooks UINavigationFocus.OnPointerUp (mouse) and PrimaryPress (controller/keyboard submit) —
/// some subclasses handle the click itself in Denuvo-protected code (UITab.OnPointerDown), but
/// these base methods are hookable and run for the same click. One click can reach both, so a
/// click on the same element is reported once per frame.
/// </summary>
internal static class UiClicks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PointerFn(IntPtr self, IntPtr eventData, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PressFn(IntPtr self, IntPtr method);

    private static NativeHook<PointerFn> _pointerUpHook;
    private static NativeHook<PressFn> _primaryPressHook;
    private static IntPtr _lastFocus;
    private static int _lastFrame = -1;

    /// <summary>Raised on the main thread after the game handled a click; the argument is the UINavigationFocus pointer.</summary>
    public static event Action<IntPtr> Clicked;

    public static bool Install()
    {
        var f = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        _pointerUpHook ??= NativeHook<PointerFn>.TryCreate(typeof(UINavigationFocus).GetMethod(nameof(UINavigationFocus.OnPointerUp), f), OnPointerUp);
        _primaryPressHook ??= NativeHook<PressFn>.TryCreate(typeof(UINavigationFocus).GetMethod(nameof(UINavigationFocus.PrimaryPress), f), OnPrimaryPress);
        return _pointerUpHook != null;
    }

    /// <summary>Forget subscribers and hooks (the hooks themselves are disposed by Hooks.DisposeAll).</summary>
    public static void Reset()
    {
        Clicked = null;
        _pointerUpHook = null;
        _primaryPressHook = null;
    }

    private static void OnPointerUp(IntPtr self, IntPtr eventData, IntPtr method)
    {
        _pointerUpHook.Original(self, eventData, method);
        Raise(self);
    }

    private static void OnPrimaryPress(IntPtr self, IntPtr method)
    {
        _primaryPressHook.Original(self, method);
        Raise(self);
    }

    private static void Raise(IntPtr focus)
    {
        var handlers = Clicked;
        if (handlers == null) return;
        int frame = UnityEngine.Time.frameCount;
        if (focus == _lastFocus && frame == _lastFrame) return;
        _lastFocus = focus;
        _lastFrame = frame;
        foreach (Action<IntPtr> h in handlers.GetInvocationList())
        {
            try { h(focus); }
            catch (Exception e) { Entry.Host.Log.LogError($"[ui] click handler {h.Method.DeclaringType?.Name}.{h.Method.Name} failed: {e}"); }
        }
    }
}
