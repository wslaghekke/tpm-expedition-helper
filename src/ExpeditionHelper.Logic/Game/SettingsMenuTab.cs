using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using ExpeditionHelper.Logic.Hooking;
using TPS.Core.UI;
using TPS.Game.UI;
using UnityEngine;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// A tab of our own in the game's Settings menu, built from clones of the game's own widgets:
/// a copy of the last tab button, a page with the Game page's scroll view, and rows cloned from
/// its "Disable Camera Shake" toggle and "Max Guest Limit" dropdown.
///
/// We can't attach handlers to those widgets (no managed → il2cpp delegates under Denuvo), so
/// clicks are caught with detours on the widget classes and matched against our instances:
///  - tab click: <see cref="UiClicks"/> (the tab's own click handler, UITab.OnPointerDown, sits
///    in Denuvo-protected code and can't be hooked);
///  - toggles: WidgetToggleSlider.OnButtonPressed; dropdowns: <see cref="UiDropdowns"/>.
/// The menu itself switches pages with SetMode(SettingsMode), which knows nothing about our
/// page: a detour on it hides our page whenever the game shows one of its own, and
/// ResetPreferences ("Revert To Default") is redirected to our defaults while our page is shown.
///
/// The Settings menu is instantiated from a prefab each time it opens, so the tab is injected
/// again in a detour on PreferencesMenu.OpenMenu.
/// </summary>
internal static class SettingsMenuTab
{
    private const string TabName = "tabExpeditionHelper";
    private const string PageName = "ExpeditionHelperContainer";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void InstanceFn(IntPtr self, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetModeFn(IntPtr self, int mode, IntPtr method);

    private static NativeHook<InstanceFn> _openMenuHook, _resetHook, _togglePressedHook;
    private static NativeHook<SetModeFn> _setModeHook;

    private static string _title;
    private static string _iconSprite;
    private static Action<SettingsPage> _build;
    private static Action _resetToDefaults;
    private static Sprite _icon;

    /// <summary>The injected tab in the currently open Settings menu, if any.</summary>
    private static Injected _current;

    private sealed class Injected
    {
        public PreferencesMenu Menu;
        public IconAndTooltipTab Tab;
        public GameObject Page;
        public SettingsPage Content;
        public readonly Dictionary<IntPtr, PreferencesMenu.SettingsMode> GameTabs = new();
        public bool Shown;
    }

    /// <param name="build">Adds the page's rows; called each time the Settings menu opens.</param>
    /// <param name="resetToDefaults">"Revert To Default" while our tab is shown.</param>
    public static void Install(string title, string iconSprite, Action<SettingsPage> build, Action resetToDefaults)
    {
        _title = title;
        _iconSprite = iconSprite;
        _build = build;
        _resetToDefaults = resetToDefaults;

        var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        _openMenuHook = NativeHook<InstanceFn>.TryCreate(typeof(PreferencesMenu).GetMethod(nameof(PreferencesMenu.OpenMenu), f), OnOpenMenu);
        _setModeHook = NativeHook<SetModeFn>.TryCreate(typeof(PreferencesMenu).GetMethod(nameof(PreferencesMenu.SetMode), f), OnSetMode);
        _resetHook = NativeHook<InstanceFn>.TryCreate(typeof(PreferencesMenu).GetMethod(nameof(PreferencesMenu.ResetPreferences), f), OnResetPreferences);
        _togglePressedHook = NativeHook<InstanceFn>.TryCreate(typeof(WidgetToggleSlider).GetMethod(nameof(WidgetToggleSlider.OnButtonPressed), f), OnTogglePressed);
        UiDropdowns.Install();
        if (_openMenuHook == null || _setModeHook == null || !UiClicks.Install())
        {
            Entry.Host.Log.LogError("[settings] the in-game settings tab is unavailable (see hook errors above); edit the .cfg instead");
            return;
        }

        UiClicks.Clicked += TabClicked;
        UiDropdowns.Selected += OnDropdownSelected;

        // Hot reload while the menu is open.
        var open = UnityEngine.Object.FindObjectOfType<PreferencesMenu>();
        if (open != null) Inject(open);
    }

    /// <summary>Remove the tab from an open Settings menu (before the hooks are disposed).</summary>
    public static void Uninstall()
    {
        var cur = _current;
        _current = null;
        if (cur == null) return;
        try
        {
            if (cur.Menu == null) return; // the menu was closed (destroyed) along with our objects
            if (cur.Shown) cur.Menu.SetMode(cur.Menu._mode); // bring the game's page back
            // Destroy is deferred to the end of the frame: rename first so a logic version loaded
            // in the same frame doesn't find the old tab and skip injecting its own.
            // (Unity's == null, not ?.: wrappers of destroyed objects aren't C# null.)
            foreach (var go in new[] { cur.Tab != null ? cur.Tab.gameObject : null, cur.Page })
            {
                if (go == null) continue;
                go.name = "(removed)";
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
            }
        }
        catch (Exception e) { Entry.Host.Log.LogError($"[settings] removing the tab failed: {e}"); }
    }

    /// <summary>Re-read every row's value (e.g. after the .cfg was edited).</summary>
    public static void Refresh()
    {
        try { if (_current != null && _current.Page != null) _current.Content.Refresh(); }
        catch (Exception e) { Entry.Host.Log.LogError($"[settings] refresh failed: {e}"); }
    }

    private static void Inject(PreferencesMenu menu)
    {
        // Clicking a cloned tab also runs the game's handler of the tab it was copied from (it
        // selects that page; Show then hides it again): copy the Game tab, whose page is the
        // cheapest to show — the Streaming page would e.g. query Twitch.
        var templateTab = menu._buttonGame;
        var tabs = templateTab.transform.parent;
        if (tabs.Find(TabName) != null) return;
        var panel = menu._panelGame;
        var pages = panel.transform.parent;

        var cur = new Injected { Menu = menu };

        // Tab button: a copy of the last tab, with our icon and tooltip.
        var tabGo = UnityEngine.Object.Instantiate(templateTab.gameObject, tabs);
        tabGo.name = TabName;
        tabGo.transform.SetAsLastSibling();
        cur.Tab = tabGo.GetComponent<IconAndTooltipTab>();
        cur.Tab.Tooltip = _title;
        var icon = Icon();
        var iconImage = tabGo.transform.Find("Icon")?.GetComponent<UnityEngine.UI.Image>();
        if (icon != null && iconImage != null) iconImage.sprite = icon;
        cur.Tab.SetState(UINavigationFocus.EState.Normal, true);

        // Page: a bare container (no PreferencesPanel component) laid out like the Game page,
        // holding a copy of its scroll view with the rows removed.
        cur.Page = new GameObject(PageName);
        var rt = cur.Page.AddComponent<RectTransform>();
        rt.SetParent(pages, false);
        var srcRt = panel.GetComponent<RectTransform>();
        rt.anchorMin = srcRt.anchorMin;
        rt.anchorMax = srcRt.anchorMax;
        rt.pivot = srcRt.pivot;
        rt.anchoredPosition = srcRt.anchoredPosition;
        rt.sizeDelta = srcRt.sizeDelta;
        var scroll = UnityEngine.Object.Instantiate(panel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true).gameObject, cur.Page.transform);
        // The Game page's scroll view is a nested Canvas with its own GraphicRaycaster. The game's
        // tooltip hover check doesn't know about a copied one (clicks work, tooltips don't), so
        // let the copy render and raycast through the menu's canvas instead.
        UnityEngine.Object.DestroyImmediate(scroll.GetComponent<UnityEngine.UI.GraphicRaycaster>());
        UnityEngine.Object.DestroyImmediate(scroll.GetComponent<Canvas>());
        var rows = scroll.GetComponent<UnityEngine.UI.ScrollRect>().content;
        for (int i = rows.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(rows.GetChild(i).gameObject);

        var toggleTemplate = panel._toggleDisableCameraShake.gameObject;
        var dropdownTemplate = UiTemplates.RowOf(panel._guestCapDropdown.transform);
        cur.Content = new SettingsPage(rows, toggleTemplate, dropdownTemplate);
        _build(cur.Content);
        cur.Content.Refresh();
        cur.Page.SetActive(false);

        void Map(UITab tab, PreferencesMenu.SettingsMode mode) { if (tab != null) cur.GameTabs[tab.Pointer] = mode; }
        Map(menu._buttonGame, PreferencesMenu.SettingsMode.Game);
        Map(menu._buttonVideo, PreferencesMenu.SettingsMode.Video);
        Map(menu._buttonAudio, PreferencesMenu.SettingsMode.Audio);
        Map(menu._buttonControls, PreferencesMenu.SettingsMode.Controls);
        Map(menu._buttonStreaming, PreferencesMenu.SettingsMode.Streaming);
        _current = cur;
    }

    private static Sprite Icon()
    {
        if (_icon != null) return _icon;
        foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            if (s.name == _iconSprite) return _icon = s;
        return null;
    }

    private static void Show(Injected cur)
    {
        var menu = cur.Menu;
        if (menu._currentPanel != null)
        {
            menu._currentPanel.Hide(); // closes dropdowns etc. but leaves the page visible
            menu._currentPanel.gameObject.SetActive(false);
        }
        foreach (var tab in cur.Tab.transform.parent.GetComponentsInChildren<UITab>())
            tab.SetState(UINavigationFocus.EState.Normal, true);
        cur.Tab.SetState(UINavigationFocus.EState.Selected, true);
        // Leave the title's Localize enabled: the game re-localizes it when it shows its own page.
        var title = menu._titleLocalize?.GetComponent<TMPro.TMP_Text>();
        if (title != null) title.text = _title;
        cur.Content.Refresh();
        cur.Page.SetActive(true);
        cur.Shown = true;
    }

    private static void Hide(Injected cur)
    {
        cur.Shown = false;
        cur.Page.SetActive(false);
        cur.Tab.SetState(UINavigationFocus.EState.Normal, true);
        cur.Menu._titleLocalize?.OnLocalize(true);
    }

    /// <summary>_current if it belongs to this (still existing) menu.</summary>
    private static Injected For(IntPtr menu)
    {
        var cur = _current;
        return cur != null && cur.Menu.Pointer == menu && cur.Page != null ? cur : null;
    }

    // ---- detours -------------------------------------------------------------------------

    private static void OnOpenMenu(IntPtr self, IntPtr method)
    {
        _openMenuHook.Original(self, method);
        try { Inject(new PreferencesMenu(self)); }
        catch (Exception e) { Entry.Host.Log.LogError($"[settings] adding the tab failed: {e}"); }
    }

    private static void OnSetMode(IntPtr self, int mode, IntPtr method)
    {
        try
        {
            var cur = For(self);
            if (cur != null && cur.Shown) Hide(cur);
        }
        catch (Exception e) { Entry.Host.Log.LogError($"[settings] SetMode detour failed: {e}"); }
        _setModeHook.Original(self, mode, method);
    }

    private static void OnResetPreferences(IntPtr self, IntPtr method)
    {
        try
        {
            var cur = For(self);
            if (cur != null && cur.Shown)
            {
                _resetToDefaults();
                cur.Content.Refresh();
                return;
            }
        }
        catch (Exception e) { Entry.Host.Log.LogError($"[settings] reset failed: {e}"); return; }
        _resetHook.Original(self, method);
    }

    /// <summary>After any UI element was clicked: act if it was one of the menu's tabs.</summary>
    private static void TabClicked(IntPtr focus)
    {
        var cur = _current;
        if (cur == null) return;
        try
        {
            if (focus == cur.Tab.Pointer)
            {
                if (cur.Page != null && !cur.Shown) Show(cur);
            }
            else if (cur.Shown && cur.GameTabs.TryGetValue(focus, out var mode))
            {
                // The game's tab handler ignores a click on the mode it thinks is still selected.
                if (cur.Shown) cur.Menu.SetMode(mode);
            }
        }
        catch (Exception e) { Entry.Host.Log.LogError($"[settings] tab click failed: {e}"); }
    }

    private static void OnTogglePressed(IntPtr self, IntPtr method)
    {
        _togglePressedHook.Original(self, method);
        try { _current?.Content?.OnToggle(self); }
        catch (Exception e) { Entry.Host.Log.LogError($"[settings] toggle failed: {e}"); }
    }

    private static void OnDropdownSelected(IntPtr dropdown) => _current?.Content?.OnDropdown(dropdown);
}

/// <summary>The rows of our settings page.</summary>
internal sealed class SettingsPage
{
    private readonly Transform _rows;
    private readonly GameObject _toggleTemplate, _dropdownTemplate;
    private readonly TooltipSpawner _tooltipTemplate;
    private readonly List<Action> _refreshers = new();
    private readonly Dictionary<IntPtr, Action<bool>> _toggles = new();
    private readonly Dictionary<IntPtr, Action<int>> _dropdowns = new();

    public SettingsPage(Transform rows, GameObject toggleTemplate, GameObject dropdownTemplate)
    {
        _rows = rows;
        _toggleTemplate = toggleTemplate;
        _dropdownTemplate = dropdownTemplate;
        // The game's toggle rows have no tooltips and their spawner uses a right-pointing tooltip
        // that covers the label; use the dropdown rows' one (pointing left, from the right side).
        _tooltipTemplate = dropdownTemplate.GetComponentInChildren<TooltipSpawner>(true);
    }

    public void Header(string text)
    {
        var row = Clone(_toggleTemplate, text, null);
        row.GetComponent<WidgetToggleSlider>().enabled = false;
        var control = row.GetComponent<WidgetToggleSlider>()._button?.transform.parent;
        if (control != null) control.gameObject.SetActive(false);
        var label = Label(row);
        label.fontSize *= 1.2f;
    }

    /// <param name="enabled">Greys the row out while false (e.g. a sub-option of a disabled feature).</param>
    public void Toggle(string text, string tooltip, Func<bool> get, Action<bool> set, Func<bool> enabled = null)
    {
        var row = Clone(_toggleTemplate, text, tooltip);
        var widget = row.GetComponent<WidgetToggleSlider>();
        widget.OnValueChanged = new UnityEngine.Events.UnityEvent<bool>(); // no copied listeners
        _toggles[widget.Pointer] = set;
        _refreshers.Add(() =>
        {
            bool value = get();
            if (widget._isOn != value) widget.SetAndSnap(value);
            widget.interactable = enabled?.Invoke() ?? true;
        });
    }

    /// <param name="options">The choices as shown; re-read on every refresh.</param>
    /// <param name="get">Index of the current choice in <paramref name="options"/>.</param>
    public void Choice(string text, string tooltip, Func<string[]> options, Func<int> get, Action<int> set, Func<bool> enabled = null)
    {
        var row = Clone(_dropdownTemplate, text, tooltip);
        var dropdown = row.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
        dropdown.onValueChanged = new TMPro.TMP_Dropdown.DropdownEvent();
        _dropdowns[dropdown.Pointer] = set;
        string[] shown = null;
        _refreshers.Add(() =>
        {
            var now = options();
            if (shown == null || !System.Linq.Enumerable.SequenceEqual(shown, now))
            {
                shown = now;
                dropdown.ClearOptions();
                var list = new Il2CppSystem.Collections.Generic.List<string>();
                foreach (var o in now) list.Add(o);
                dropdown.AddOptions(list);
            }
            dropdown.SetValueWithoutNotify(get());
            dropdown.interactable = enabled?.Invoke() ?? true;
        });
    }

    public void Refresh()
    {
        foreach (var r in _refreshers) r();
    }

    internal void OnToggle(IntPtr widget)
    {
        if (!_toggles.TryGetValue(widget, out var set)) return;
        set(new WidgetToggleSlider(widget)._isOn);
        Refresh(); // dependent rows
    }

    internal void OnDropdown(IntPtr dropdown)
    {
        if (!_dropdowns.TryGetValue(dropdown, out var set)) return;
        set(new TMPro.TMP_Dropdown(dropdown).value);
        Refresh();
    }

    private GameObject Clone(GameObject template, string text, string tooltip)
    {
        var row = UnityEngine.Object.Instantiate(template, _rows);
        row.name = "EH_" + text;
        row.SetActive(true);
        // Every label in a cloned row is ours now (incl. dropdown items): stop I2 re-translating them.
        foreach (var l in row.GetComponentsInChildren<I2.Loc.Localize>(true)) l.enabled = false;
        UiText.Set(Label(row), text);
        foreach (var spawner in row.GetComponentsInChildren<TooltipSpawner>(true))
        {
            spawner._tooltip = UiText.Plain(Wrap(tooltip));
            if (_tooltipTemplate != null)
            {
                spawner._prefab = _tooltipTemplate._prefab;
                spawner._prefabConsole = _tooltipTemplate._prefabConsole;
                spawner._anchorOffset = _tooltipTemplate._anchorOffset;
                var from = _tooltipTemplate._anchorTransform?.GetComponent<RectTransform>();
                var to = spawner._anchorTransform?.GetComponent<RectTransform>();
                if (from != null && to != null)
                {
                    to.anchorMin = from.anchorMin;
                    to.anchorMax = from.anchorMax;
                    to.pivot = from.pivot;
                    to.anchoredPosition = from.anchoredPosition;
                }
            }
            spawner.enabled = tooltip != null;
        }
        return row;
    }

    /// <summary>
    /// Break a description into lines: this tooltip doesn't wrap by itself (the game writes its
    /// own tooltip texts with explicit line breaks).
    /// </summary>
    private static string Wrap(string text, int width = 60)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder();
        int line = 0;
        foreach (var word in text.Split(' '))
        {
            if (line > 0 && line + 1 + word.Length > width) { sb.Append('\n'); line = 0; }
            else if (line > 0) { sb.Append(' '); line++; }
            sb.Append(word);
            line += word.Length;
        }
        return sb.ToString();
    }

    private static TMPro.TMP_Text Label(GameObject row) =>
        row.transform.Find("ContainerBG/TextOptionName")?.GetComponent<TMPro.TMP_Text>()
        ?? row.GetComponentInChildren<TMPro.TMP_Text>(true);
}
