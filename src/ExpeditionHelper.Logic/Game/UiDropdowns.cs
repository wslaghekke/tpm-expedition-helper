using System;
using System.Reflection;
using System.Runtime.InteropServices;
using ExpeditionHelper.Logic.Hooking;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Choices made in any TMP_Dropdown, so cloned dropdowns can react without handing delegates to
/// the game (not possible under Denuvo): a detour on TMP_Dropdown.OnSelectItem, which runs when
/// an item in the open list is picked. Subscribers match the dropdown pointer against their own.
/// </summary>
internal static class UiDropdowns
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SelectItemFn(IntPtr self, IntPtr toggle, IntPtr method);

    private static NativeHook<SelectItemFn> _selectItemHook;

    /// <summary>Raised after the game applied the choice; the argument is the TMP_Dropdown pointer (read its `value`).</summary>
    public static event Action<IntPtr> Selected;

    public static bool Install()
    {
        var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        _selectItemHook ??= NativeHook<SelectItemFn>.TryCreate(typeof(TMPro.TMP_Dropdown).GetMethod("OnSelectItem", f), OnSelectItem);
        return _selectItemHook != null;
    }

    /// <summary>Forget subscribers and the hook (the hook itself is disposed by Hooks.DisposeAll).</summary>
    public static void Reset()
    {
        Selected = null;
        _selectItemHook = null;
    }

    private static void OnSelectItem(IntPtr self, IntPtr toggle, IntPtr method)
    {
        _selectItemHook.Original(self, toggle, method);
        var handlers = Selected;
        if (handlers == null) return;
        foreach (Action<IntPtr> h in handlers.GetInvocationList())
        {
            try { h(self); }
            catch (Exception e) { Entry.Host.Log.LogError($"[ui] dropdown handler {h.Method.DeclaringType?.Name}.{h.Method.Name} failed: {e}"); }
        }
    }
}
