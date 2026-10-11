using System;
using System.Reflection;
using System.Runtime.InteropServices;
using ExpeditionHelper.Logic.Hooking;
using TPS.Game.UI;
using UnityEngine;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// A section of our own on the expedition setup screen (ExpeditionMenu_Setup_V2), below
/// "Choose Cargo": a header cloned from the cargo header, an On/Off toggle row (cloned from the
/// Settings menu, see <see cref="UiTemplates"/>) and a status text.
///
/// Injected in a detour on the setup screen's OpenMenu; clicks come through <see cref="UiClicks"/>.
/// </summary>
internal static class ExpeditionSetupSection
{
    private const string SectionName = "containerExpeditionHelper";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OpenMenuFn(IntPtr self, IntPtr settings, IntPtr eventsMenu, IntPtr teamSelectMenu,
        IntPtr cargoSelectMenu, IntPtr exhibitSelectMenu, IntPtr onClosed, IntPtr method);

    private static NativeHook<OpenMenuFn> _openMenuHook;
    private static string _title, _label;
    private static Section _current;

    private sealed class Section
    {
        public ExpeditionMenu_Setup_V2 Menu;
        public GameObject Root;
        public WidgetToggleSlider Toggle;
        public IntPtr Button;      // the toggle's clickable area
        public TMPro.TMP_Text Status;
        public UnityEngine.UI.LayoutElement StatusLayout;
    }

    /// <summary>Raised when the player flips the toggle, with its new value.</summary>
    public static event Action<bool> Toggled;

    /// <summary>The open setup screen showing our section, or null.</summary>
    public static ExpeditionMenu_Setup_V2 OpenMenu
    {
        get
        {
            var cur = _current;
            return cur != null && cur.Root != null && cur.Menu.IsOpen && cur.Root.transform.parent.gameObject.activeInHierarchy ? cur.Menu : null;
        }
    }

    public static bool Install(string title, string label)
    {
        _title = title;
        _label = label;
        var f = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        _openMenuHook = NativeHook<OpenMenuFn>.TryCreate(typeof(ExpeditionMenu_Setup_V2).GetMethod(nameof(ExpeditionMenu_Setup_V2.OpenMenu), f), OnOpenMenu);
        if (_openMenuHook == null || !UiClicks.Install()) return false;
        UiClicks.Clicked += OnClicked;

        var open = UnityEngine.Object.FindObjectOfType<ExpeditionMenu_Setup_V2>();
        if (open != null) Inject(open);
        return true;
    }

    public static void Uninstall()
    {
        var cur = _current;
        _current = null;
        Toggled = null;
        _openMenuHook = null; // disposed by Hooks.DisposeAll
        if (cur?.Root == null) return;
        cur.Root.name = "(removed)"; // Destroy is deferred: don't let a reload in this frame find it
        cur.Root.SetActive(false);
        UnityEngine.Object.Destroy(cur.Root);
    }

    /// <summary>Show or hide the section, and set the toggle (greyed out unless enabled) and status text.</summary>
    public static void Set(bool visible, bool on, bool enabled, string status)
    {
        var cur = _current;
        if (cur == null || cur.Root == null) return;
        if (cur.Root.activeSelf != visible) cur.Root.SetActive(visible);
        if (!visible) return;
        if (cur.Toggle._isOn != on) cur.Toggle.SetAndSnap(on);
        if (cur.Toggle.interactable != enabled) cur.Toggle.interactable = enabled;
        if (cur.Status.text != status)
        {
            cur.Status.text = status;
            float width = cur.Status.rectTransform.rect.width;
            if (width <= 0) width = 900; // not laid out yet
            cur.StatusLayout.preferredHeight = cur.Status.GetPreferredValues(status, width, 0).y + 20;
        }
    }

    private static void OnOpenMenu(IntPtr self, IntPtr settings, IntPtr eventsMenu, IntPtr teamSelectMenu,
        IntPtr cargoSelectMenu, IntPtr exhibitSelectMenu, IntPtr onClosed, IntPtr method)
    {
        _openMenuHook.Original(self, settings, eventsMenu, teamSelectMenu, cargoSelectMenu, exhibitSelectMenu, onClosed, method);
        try { Inject(new ExpeditionMenu_Setup_V2(self)); }
        catch (Exception e) { Entry.Host.Log.LogError($"[setup] adding the repeat section failed: {e}"); }
    }

    private static void OnClicked(IntPtr focus)
    {
        var cur = _current;
        // The game has already flipped the slider when this runs.
        if (cur != null && focus == cur.Button && cur.Root != null && cur.Root.activeInHierarchy && cur.Toggle.interactable)
            Toggled?.Invoke(cur.Toggle._isOn);
    }

    private static void Inject(ExpeditionMenu_Setup_V2 menu)
    {
        var content = menu._buttonAutoFill.transform.parent;
        if (_current != null && _current.Root != null && _current.Menu.Pointer == menu.Pointer) return;
        var existing = content.Find(SectionName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject); // from an older logic version

        var cargo = menu._cargoSlot.transform.parent; // containerCargo: header + cargo slot
        var toggleTemplate = UiTemplates.ToggleRow();
        if (toggleTemplate == null) { Entry.Host.Log.LogError("[setup] no toggle template; repeat option unavailable"); return; }

        // Section: a copy of the cargo section (header and layout) without its content.
        var root = UnityEngine.Object.Instantiate(cargo.gameObject, content);
        root.name = SectionName;
        root.transform.SetSiblingIndex(cargo.GetSiblingIndex() + 1);
        for (int i = root.transform.childCount - 1; i >= 1; i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        var header = root.transform.GetChild(0).GetComponentInChildren<TMPro.TMP_Text>(true);
        UiText.Set(header, _title);

        // Toggle row: label + On/Off slider, fitted to this narrower panel.
        var row = NewRow(root.transform, "rowToggle", 130);
        var toggleRow = UnityEngine.Object.Instantiate(toggleTemplate, row.transform);
        toggleRow.name = "toggleRepeat";
        toggleRow.SetActive(true);
        var rt = toggleRow.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.offsetMin = new Vector2(20, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-120, rt.offsetMax.y);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0);
        foreach (var l in toggleRow.GetComponentsInChildren<I2.Loc.Localize>(true)) l.enabled = false;
        foreach (var spawner in toggleRow.GetComponentsInChildren<TooltipSpawner>(true)) spawner.enabled = false;
        var labelText = toggleRow.transform.Find("ContainerBG/TextOptionName").GetComponent<TMPro.TMP_Text>();
        UiText.Set(labelText, _label);
        // The label is sized for the Settings page's much wider rows: leave room for the slider.
        labelText.rectTransform.sizeDelta = new Vector2(-440, labelText.rectTransform.sizeDelta.y);
        var toggle = toggleRow.GetComponent<WidgetToggleSlider>();
        toggle.OnValueChanged = new UnityEngine.Events.UnityEvent<bool>(); // no copied listeners

        // Status text: a copy of the label, smaller and wrapping.
        var statusRow = NewRow(root.transform, "rowStatus", 60);
        var status = UnityEngine.Object.Instantiate(labelText.gameObject, statusRow.transform).GetComponent<TMPro.TMP_Text>();
        status.name = "textStatus";
        UiText.Set(status, "");
        status.fontSize = labelText.fontSize * 0.7f;
        status.enableWordWrapping = true;
        status.alignment = TMPro.TextAlignmentOptions.TopLeft;
        var srt = status.rectTransform;
        srt.anchorMin = new Vector2(0, 0);
        srt.anchorMax = new Vector2(1, 1);
        srt.pivot = new Vector2(0, 1);
        srt.offsetMin = new Vector2(45, 0);
        srt.offsetMax = new Vector2(-140, 0); // the row is wider than the visible panel

        _current = new Section
        {
            Menu = menu,
            Root = root,
            Toggle = toggle,
            Button = toggle._button != null ? toggle._button.Pointer : IntPtr.Zero,
            Status = status,
            StatusLayout = statusRow.GetComponent<UnityEngine.UI.LayoutElement>(),
        };
    }

    private static GameObject NewRow(Transform parent, string name, float height)
    {
        var row = new GameObject(name);
        row.AddComponent<RectTransform>().SetParent(parent, false);
        var layout = row.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        return row;
    }


}
