using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ExpeditionHelper.Logic.Hooking;
using TPS.Game.UI;
using UnityEngine;

namespace ExpeditionHelper.Logic.Workshop;

/// <summary>
/// Widens the Start Workshop Project menu: its category tabs overflow the 800-wide project list
/// (they sit in a horizontal scroll view), so the list gets wider and shows 3 project cards per
/// row instead of 2, and the project detail panel next to it moves over.
///
/// Applied from WorkshopStartProjectMenuScrollView.SetContents, which runs when the menu opens and
/// on every tab switch, right before the game lays out the cards. Original sizes are remembered
/// per object, so turning the setting off (or unloading the logic) puts the menu back.
/// </summary>
internal static class WorkshopLayout
{
    private const float ExtraWidth = 220f;
    private const int WideColumns = 3;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetContentsFn(IntPtr self, IntPtr menu, IntPtr projects, [MarshalAs(UnmanagedType.U1)] bool resetScroll, IntPtr method);

    private static NativeHook<SetContentsFn> _hook;
    private static WorkshopSettings _settings;
    private static WorkshopStartProjectMenuScrollView _last; // to apply setting changes while the menu is open

    private static readonly Dictionary<IntPtr, (Vector2 size, Vector2 pivot)> OriginalRects = new();
    private static readonly Dictionary<IntPtr, int> OriginalColumns = new();

    public static void Install(WorkshopSettings settings)
    {
        _settings = settings;
        _hook = NativeHook<SetContentsFn>.TryCreate(
            typeof(WorkshopStartProjectMenuScrollView).GetMethod(nameof(WorkshopStartProjectMenuScrollView.SetContents)), SetContents);
    }

    /// <summary>Re-apply after the setting changed. Main thread.</summary>
    public static void Refresh()
    {
        try
        {
            if (_last == null) return;
            Apply(_last, _settings.WiderProjectMenu.Value);
            _last.ScrollViewContent.Refresh();
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[workshop] refresh failed: {e}");
        }
    }

    public static void Uninstall()
    {
        try
        {
            if (_last != null)
            {
                Apply(_last, wide: false);
                _last.ScrollViewContent.Refresh();
            }
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[workshop] restoring the menu failed: {e}");
        }
        _last = null;
        OriginalRects.Clear();
        OriginalColumns.Clear();
    }

    private static void SetContents(IntPtr self, IntPtr menu, IntPtr projects, bool resetScroll, IntPtr method)
    {
        try
        {
            var view = new WorkshopStartProjectMenuScrollView(self);
            _last = view;
            Apply(view, _settings.WiderProjectMenu.Value);
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[workshop] {e}");
        }
        _hook.Original(self, menu, projects, resetScroll, method);
    }

    private static void Apply(WorkshopStartProjectMenuScrollView view, bool wide)
    {
        var content = view.ScrollViewContent;
        if (content != null)
        {
            if (!OriginalColumns.TryGetValue(content.Pointer, out int columns))
                OriginalColumns[content.Pointer] = columns = content._numPerRow;
            content._numPerRow = wide ? WideColumns : columns;
        }

        var menu = view._startProjectMenu;
        if (menu == null) return;
        var list = Panel(menu.transform, "ProjectSelectionPanel");
        var detail = Panel(menu.transform, "ProjectDetailPanel");
        float extra = wide ? ExtraWidth : 0f;

        // The panels' open/close animation sets their anchoredPosition, so they're moved through
        // the pivot instead (the point anchoredPosition places): same position, different edges.
        if (list != null)
        {
            var (size, pivot) = Original(list);
            float width = list.rect.width - list.sizeDelta.x + size.x; // anchors may add to sizeDelta
            list.sizeDelta = new Vector2(size.x + extra, size.y);
            list.pivot = new Vector2(pivot.x * width / (width + extra), pivot.y); // left edge stays put
        }
        if (detail != null)
        {
            var (_, pivot) = Original(detail);
            detail.pivot = new Vector2(pivot.x - extra / detail.rect.width, pivot.y); // moves right by `extra`
        }
    }

    private static RectTransform Panel(Transform menu, string name)
    {
        var t = menu.Find(name);
        return t == null ? null : t.TryCast<RectTransform>();
    }

    private static (Vector2 size, Vector2 pivot) Original(RectTransform rt)
    {
        if (!OriginalRects.TryGetValue(rt.Pointer, out var original))
            OriginalRects[rt.Pointer] = original = (rt.sizeDelta, rt.pivot);
        return original;
    }
}
