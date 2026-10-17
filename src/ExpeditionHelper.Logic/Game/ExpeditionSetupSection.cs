using System;
using System.Reflection;
using System.Runtime.InteropServices;
using ExpeditionHelper.Logic.Hooking;
using TPS.Game.UI;
using UnityEngine;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// A section of our own on the expedition setup screen (ExpeditionMenu_Setup_V2), below
/// "Choose Cargo": a header cloned from the cargo header, an On/Off toggle row and a goal (perk/stock)
/// dropdown row (both cloned from the Settings menu, see <see cref="UiTemplates"/>) and a status text.
///
/// Injected in a detour on the setup screen's OpenMenu; clicks come through <see cref="UiClicks"/>,
/// dropdown choices through <see cref="UiDropdowns"/>.
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
        public TMPro.TMP_Text ToggleLabel;
        public UnityEngine.UI.LayoutElement StatusLayout;
        public GameObject GoalRow;        // null if the dropdown template wasn't available
        public TMPro.TMP_Dropdown GoalDropdown;
        public string[] GoalOptions;
        public TMPro.TMP_Text GoalLabel;
        public GameObject QuantityRow;    // null if the workshop's quantity row wasn't available
        public TPS.Core.UI.UIButtonGroup QuantityGroup;
        public TPS.Core.UI.UIButton[] QuantityButtons;
        public TMPro.TMP_Text[] QuantityLabels;
        public float QuantityFitWidth; // bar width the label size was last fitted for
        public TPS.Core.UI.UIButton LastTeam; // copy of Auto-fill; null if that wasn't available
        public GameObject TeamRow;           // holds Auto-fill and Last team side by side
    }

    /// <summary>Raised when the player flips the toggle, with its new value.</summary>
    public static event Action<bool> Toggled;

    /// <summary>Raised when the player picks an entry in the goal dropdown, with its index.</summary>
    public static event Action<int> GoalChosen;

    /// <summary>The counts the quantity row offers, in button order (like the workshop's).</summary>
    public static readonly int[] Quantities = { 1, 3, 5, 10 };

    /// <summary>Raised when the player picks a count in the quantity row, with the count.</summary>
    public static event Action<int> QuantityChosen;

    /// <summary>Raised when the player clicks the "Last team" button.</summary>
    public static event Action LastTeamClicked;

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
        if (UiDropdowns.Install()) UiDropdowns.Selected += OnDropdownSelected;

        var open = UnityEngine.Object.FindObjectOfType<ExpeditionMenu_Setup_V2>();
        if (open != null) Inject(open);
        return true;
    }

    public static void Uninstall()
    {
        var cur = _current;
        _current = null;
        Toggled = null;
        GoalChosen = null;
        QuantityChosen = null;
        LastTeamClicked = null;
        _openMenuHook = null; // disposed by Hooks.DisposeAll
        if (cur != null) RemoveTeamRow(cur.Menu);
        if (cur?.Root == null) return;
        Remove(cur.Root);
    }

    private static void Remove(GameObject go)
    {
        go.name = "(removed)"; // Destroy is deferred: don't let a reload in this frame find it
        go.SetActive(false);
        UnityEngine.Object.Destroy(go);
    }

    /// <summary>The "Last team" button: shown only while Auto-fill is, greyed out (locked) unless enabled.</summary>
    public static void SetLastTeam(bool enabled)
    {
        var cur = _current;
        if (cur == null || cur.LastTeam == null || cur.TeamRow == null) return;
        // The game shows/hides Auto-fill itself; the row (and with it Last team) follows.
        bool visible = cur.Menu._buttonAutoFill.gameObject.activeSelf;
        if (cur.TeamRow.activeSelf != visible) cur.TeamRow.SetActive(visible);
        if (cur.LastTeam.interactable != enabled) cur.LastTeam.SetLocked(!enabled);
    }

    /// <summary>
    /// Show or hide the section, and set the toggle (greyed out unless enabled; hidden without
    /// `showToggle`, where the goal dropdown alone decides) and status text.
    /// </summary>
    public static void Set(bool visible, bool on, bool enabled, string status, bool showToggle = true)
    {
        var cur = _current;
        if (cur == null || cur.Root == null) return;
        if (cur.Root.activeSelf != visible) cur.Root.SetActive(visible);
        if (!visible) return;
        var toggleRow = cur.Toggle.transform.parent.gameObject; // rowToggle
        if (toggleRow.activeSelf != showToggle) { toggleRow.SetActive(showToggle); RelayOut(cur); }
        if (cur.Toggle._isOn != on) cur.Toggle.SetAndSnap(on);
        if (cur.Toggle.interactable != enabled) cur.Toggle.interactable = enabled;
        if (cur.Status.text != status)
        {
            cur.Status.text = status;
            float width = cur.Status.rectTransform.rect.width;
            if (width <= 0) width = 900; // not laid out yet
            float height = cur.Status.GetPreferredValues(status, width, 0).y + 20;
            if (cur.StatusLayout.preferredHeight != height) { cur.StatusLayout.preferredHeight = height; RelayOut(cur); }
        }
    }

    /// <summary>
    /// Lay the screen's list out again after one of our rows appeared, disappeared or changed height:
    /// its nested auto-sized sections don't all follow by themselves (the cargo section kept a stale
    /// height and our header slid over its slot).
    /// </summary>
    private static void RelayOut(Section cur)
    {
        var content = cur.Root.transform.parent;
        if (content == null) return;
        for (int i = 0; i < content.childCount; i++)
        {
            var child = content.GetChild(i);
            if (child.gameObject.activeSelf) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(child.GetComponent<RectTransform>());
        }
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
    }

    /// <summary>The goal dropdown: hidden when `options` is null or empty, else titled `label` and showing options[index].</summary>
    public static void SetGoals(string label, string[] options, int index, bool enabled)
    {
        var cur = _current;
        if (cur == null || cur.Root == null || cur.GoalRow == null) return;
        bool visible = options != null && options.Length > 0;
        if (cur.GoalRow.activeSelf != visible) { cur.GoalRow.SetActive(visible); RelayOut(cur); }
        if (!visible) return;
        if (cur.GoalLabel != null && cur.GoalLabel.text != label) cur.GoalLabel.text = label;
        if (cur.GoalOptions == null || !System.Linq.Enumerable.SequenceEqual(cur.GoalOptions, options))
        {
            cur.GoalOptions = options;
            cur.GoalDropdown.ClearOptions();
            var list = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var o in options) list.Add(o);
            cur.GoalDropdown.AddOptions(list);
        }
        if (cur.GoalDropdown.value != index) cur.GoalDropdown.SetValueWithoutNotify(index);
        if (cur.GoalDropdown.interactable != enabled) cur.GoalDropdown.interactable = enabled;
    }

    /// <summary>
    /// The quantity row under the goal dropdown: shown or hidden, with button `index` selected and
    /// the buttons labelled `labels` (one per <see cref="Quantities"/> entry; "x1".. if null).
    /// </summary>
    public static void SetQuantity(bool visible, int index, string[] labels = null)
    {
        var cur = _current;
        if (cur == null || cur.Root == null || cur.QuantityRow == null) return;
        if (cur.QuantityRow.activeSelf != visible) { cur.QuantityRow.SetActive(visible); RelayOut(cur); }
        if (!visible) return;
        bool changed = false;
        for (int i = 0; i < cur.QuantityLabels.Length; i++)
        {
            string text = labels != null && i < labels.Length ? labels[i] : $"x{Quantities[i]}";
            if (cur.QuantityLabels[i] == null || cur.QuantityLabels[i].text == text) continue;
            cur.QuantityLabels[i].text = text;
            changed = true;
        }
        var bar = cur.QuantityGroup.GetComponent<RectTransform>();
        if (changed || bar.rect.width != cur.QuantityFitWidth)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(bar); // the buttons' widths, before fitting into them
            FitQuantityLabels(cur.QuantityLabels);
            cur.QuantityFitWidth = bar.rect.width;
        }
        if (index < 0 || index >= cur.QuantityButtons.Length) return;
        // Through the group, as a click does: it animates the previous button off (setting the
        // buttons' state directly left two green dots). And as its default: when the row first
        // shows, the group starts up after this and would select its default (x1) again.
        var button = cur.QuantityButtons[index];
        var group = cur.QuantityGroup;
        group._defaultSelected = button;
        if (group._selected == button) return;
        group.OnButtonSelected(button);
    }

    private const float QuantityFontSize = 56; // the workshop's

    /// <summary>One font size for all quantity labels: the largest at which the longest ("+1,000") fits its button.</summary>
    private static void FitQuantityLabels(TMPro.TMP_Text[] labels)
    {
        float size = QuantityFontSize;
        foreach (var label in labels)
        {
            if (label == null) continue;
            label.enableAutoSizing = false;
            label.fontSize = QuantityFontSize;
            float width = label.rectTransform.rect.width;
            float wanted = label.GetPreferredValues(label.text).x;
            if (width > 0 && wanted > width) size = Math.Min(size, QuantityFontSize * width / wanted);
        }
        foreach (var label in labels)
            if (label != null) label.fontSize = size;
    }

    /// <summary>The toggle's label ("Repeat until done", or the one goal a location has).</summary>
    public static void SetToggleLabel(string text)
    {
        var cur = _current;
        if (cur?.ToggleLabel != null && cur.ToggleLabel.text != text) cur.ToggleLabel.text = text;
    }

    /// <summary>
    /// The toggle above the goal rows (where it switches repeating on), or below the quantity row
    /// (where it's an option for the picked count, like the art studio's "wait until fully rested").
    /// </summary>
    public static void SetToggleBelowQuantity(bool below)
    {
        var cur = _current;
        if (cur?.Root == null || cur.QuantityRow == null) return;
        var toggleRow = cur.Toggle.transform.parent;
        var quantityRow = cur.QuantityRow.transform;
        if ((toggleRow.GetSiblingIndex() > quantityRow.GetSiblingIndex()) == below) return;
        toggleRow.SetSiblingIndex(below ? quantityRow.GetSiblingIndex() : 1); // 0 is the header
        RelayOut(cur);
    }

    private static void OnDropdownSelected(IntPtr dropdown)
    {
        var cur = _current;
        if (cur?.GoalDropdown == null || cur.GoalRow == null || dropdown != cur.GoalDropdown.Pointer) return;
        GoalChosen?.Invoke(cur.GoalDropdown.value);
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
        if (cur != null && cur.LastTeam != null && focus == cur.LastTeam.Pointer)
        {
            if (cur.LastTeam.interactable && cur.LastTeam.gameObject.activeInHierarchy) LastTeamClicked?.Invoke();
            return;
        }
        // The quantity row's button group has already moved its selection when this runs.
        if (cur?.QuantityButtons != null && cur.QuantityRow != null && cur.QuantityRow.activeInHierarchy)
        {
            int index = Array.FindIndex(cur.QuantityButtons, b => b != null && b.Pointer == focus);
            if (index >= 0)
            {
                int selected = cur.QuantityGroup.SelectedIndex;
                QuantityChosen?.Invoke(Quantities[selected >= 0 && selected < Quantities.Length ? selected : index]);
                return;
            }
        }
        // The game has already flipped the slider when this runs.
        if (cur != null && focus == cur.Button && cur.Root != null && cur.Root.activeInHierarchy && cur.Toggle.interactable)
            Toggled?.Invoke(cur.Toggle._isOn);
    }

    private static void Inject(ExpeditionMenu_Setup_V2 menu)
    {
        var content = ContentOf(menu);
        if (_current != null && _current.Root != null && _current.Menu.Pointer == menu.Pointer) return;
        var existing = content.Find(SectionName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject); // from an older logic version
        RemoveTeamRow(menu); // from an older logic version

        var cargo = menu._cargoSlot.transform.parent; // containerCargo: header + cargo slot
        var toggleTemplate = UiTemplates.ToggleRow();
        if (toggleTemplate == null) { Entry.Host.Log.LogError("[setup] no toggle template; repeat option unavailable"); return; }

        // Section: a copy of the cargo section (header and layout) without its content.
        var root = UnityEngine.Object.Instantiate(cargo.gameObject, content);
        root.name = SectionName;
        // Last in the list: after the art studio's "Explore Emotion" (and "Choose Material"), the
        // sections after cargo that the game shows only for some kinds of location.
        var emotion = menu._emotionContainer;
        var last = emotion != null && emotion.transform.parent == content ? emotion.transform : cargo;
        root.transform.SetSiblingIndex(last.GetSiblingIndex() + 1);
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
        labelText.enableWordWrapping = false; // "Wait until fully rested" is a few pixels wider than that room, and fits
        var toggle = toggleRow.GetComponent<WidgetToggleSlider>();
        toggle.OnValueChanged = new UnityEngine.Events.UnityEvent<bool>(); // no copied listeners

        var (goalRow, goalDropdown, goalLabel) = AddGoalRow(root.transform);
        var (quantityRow, quantityGroup, quantityButtons) = AddQuantityRow(root.transform);

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
            ToggleLabel = labelText,
            StatusLayout = statusRow.GetComponent<UnityEngine.UI.LayoutElement>(),
            GoalRow = goalRow,
            GoalDropdown = goalDropdown,
            GoalLabel = goalLabel,
            QuantityRow = quantityRow,
            QuantityGroup = quantityGroup,
            QuantityButtons = quantityButtons,
            QuantityLabels = quantityButtons == null ? null : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(quantityButtons, b => b.GetComponentInChildren<TMPro.TMP_Text>(true))),
        };
        (_current.LastTeam, _current.TeamRow) = AddLastTeamButton(menu);
    }

    private const string TeamRowName = "rowTeamButtons";
    private const float TeamButtonWidth = 460; // two of these fit the panel
    private static float _autoFillWidth = 633;  // Auto-fill's own width, restored on unload

    /// <summary>The setup screen's content list (Auto-fill's parent, unless it's in our row).</summary>
    private static Transform ContentOf(ExpeditionMenu_Setup_V2 menu)
    {
        var parent = menu._buttonAutoFill.transform.parent;
        return parent.name == TeamRowName ? parent.parent : parent;
    }

    /// <summary>
    /// "Last team": a copy of the Auto-fill button, next to it in a row that takes Auto-fill's place.
    /// The game wires Auto-fill's click at runtime on that instance, so the copy has no click
    /// handler of its own; ours comes through <see cref="UiClicks"/>.
    /// </summary>
    private static (TPS.Core.UI.UIButton, GameObject) AddLastTeamButton(ExpeditionMenu_Setup_V2 menu)
    {
        var autoFill = menu._buttonAutoFill;
        if (autoFill == null) return (null, null);
        var content = autoFill.transform.parent;

        var row = new GameObject(TeamRowName);
        row.AddComponent<RectTransform>().SetParent(content, false);
        row.transform.SetSiblingIndex(autoFill.transform.GetSiblingIndex());
        var layout = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 20;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var autoFillLayout = autoFill.GetComponent<UnityEngine.UI.LayoutElement>();
        row.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = autoFillLayout != null ? autoFillLayout.preferredHeight : 113;

        var go = UnityEngine.Object.Instantiate(autoFill.gameObject, row.transform);
        go.name = "buttonLastTeam";
        foreach (var spawner in go.GetComponentsInChildren<TooltipSpawner>(true)) spawner.enabled = false;
        foreach (var text in go.GetComponentsInChildren<TMPro.TMP_Text>(true)) UiText.Set(text, "Last team");
        autoFill.transform.SetParent(row.transform, false);
        autoFill.transform.SetAsFirstSibling();
        if (autoFillLayout != null && autoFillLayout.preferredWidth > TeamButtonWidth) _autoFillWidth = autoFillLayout.preferredWidth;
        foreach (var le in row.GetComponentsInChildren<UnityEngine.UI.LayoutElement>(true))
            if (le.gameObject != row) le.preferredWidth = TeamButtonWidth;
        return (go.GetComponent<TPS.Core.UI.UIButton>(), row);
    }

    /// <summary>Put Auto-fill back where it was (full width) and remove our row with Last team in it.</summary>
    private static void RemoveTeamRow(ExpeditionMenu_Setup_V2 menu)
    {
        var autoFill = menu != null ? menu._buttonAutoFill : null;
        if (autoFill == null) return;
        var row = autoFill.transform.parent;
        if (row.name != TeamRowName) return;
        autoFill.transform.SetParent(row.parent, false);
        autoFill.transform.SetSiblingIndex(row.GetSiblingIndex());
        var le = autoFill.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le != null) le.preferredWidth = _autoFillWidth;
        Remove(row.gameObject);
    }

    /// <summary>A label ("Repeat for perk" etc.) + dropdown, fitted like the toggle row; hidden until <see cref="SetGoals"/> shows it.</summary>
    private static (GameObject, TMPro.TMP_Dropdown, TMPro.TMP_Text) AddGoalRow(Transform section)
    {
        var template = UiTemplates.DropdownRow();
        if (template == null) { Entry.Host.Log.LogError("[setup] no dropdown template; perk/stock choice unavailable"); return (null, null, null); }

        // Label on its own line with the dropdown below it at full width: next to each other there's
        // no room for perk and material names in this narrow panel.
        var row = NewRow(section, "rowGoal", 190);
        var dropdownRow = UnityEngine.Object.Instantiate(template, row.transform);
        dropdownRow.name = "dropdownGoal";
        dropdownRow.SetActive(true);
        var rt = dropdownRow.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.offsetMin = new Vector2(20, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-120, rt.offsetMax.y);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, 189);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0);
        var bg = dropdownRow.transform.Find("ContainerBG");
        Place(bg?.Find("TextOptionName"), 1, new Vector2(24, -8), 70);
        Place(bg?.Find("Dropdown"), 0, new Vector2(0, 12), 94);
        Place(bg?.Find("Template"), 0, new Vector2(0, 12), 400, pivotY: 1); // the list opens below the dropdown
        // Every label in the clone is ours now (incl. the list items): stop I2 re-translating them.
        foreach (var l in dropdownRow.GetComponentsInChildren<I2.Loc.Localize>(true)) l.enabled = false;
        foreach (var spawner in dropdownRow.GetComponentsInChildren<TooltipSpawner>(true)) spawner.enabled = false;
        var label = dropdownRow.transform.Find("ContainerBG/TextOptionName")?.GetComponent<TMPro.TMP_Text>()
                    ?? dropdownRow.GetComponentInChildren<TMPro.TMP_Text>(true);
        UiText.Set(label, "Repeat for perk");
        var dropdown = dropdownRow.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
        dropdown.onValueChanged = new TMPro.TMP_Dropdown.DropdownEvent(); // no copied listeners
        row.SetActive(false);
        return (row, dropdown, label);
    }

    /// <summary>
    /// "x1 x3 x5 x10" under the goal dropdown: a copy of the workshop's quantity row, whose button
    /// group moves the selection itself; clicks reach us through <see cref="UiClicks"/>. Hidden until
    /// <see cref="SetQuantity"/> shows it.
    /// </summary>
    private static (GameObject, TPS.Core.UI.UIButtonGroup, TPS.Core.UI.UIButton[]) AddQuantityRow(Transform section)
    {
        var template = UiTemplates.QuantityRow();
        if (template == null) { Entry.Host.Log.LogError("[setup] no quantity template; perk amounts unavailable"); return (null, null, null); }

        var row = NewRow(section, "rowQuantity", 70);
        var copy = UnityEngine.Object.Instantiate(template, row.transform);
        copy.name = "quantity";
        copy.SetActive(true);
        // As wide as the dropdown and the toggle above it, the buttons sharing the width equally.
        var rt = copy.GetComponent<RectTransform>();
        float height = rt.rect.height;
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.offsetMin = new Vector2(44, -height / 2);
        rt.offsetMax = new Vector2(-144, height / 2);
        var layout = copy.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            // The workshop's buttons don't fill its bar; stretched they would touch its ends.
            layout.padding.left = 24;
            layout.padding.right = 24;
        }
        var hint = copy.transform.Find("ToggleBatchingPlatformImage"); // the workshop's controller shortcut
        if (hint != null) hint.gameObject.SetActive(false);
        foreach (var l in copy.GetComponentsInChildren<I2.Loc.Localize>(true)) l.enabled = false;
        foreach (var spawner in copy.GetComponentsInChildren<TooltipSpawner>(true)) spawner.enabled = false;

        var group = copy.GetComponent<TPS.Core.UI.UIButtonGroup>();
        var buttons = new System.Collections.Generic.List<TPS.Core.UI.UIButton>();
        if (group != null && group.Buttons != null) foreach (var b in group.Buttons) buttons.Add(b);
        if (group == null || buttons.Count != Quantities.Length)
        {
            Entry.Host.Log.LogError($"[setup] the workshop's quantity row has changed ({buttons.Count} buttons); perk amounts unavailable");
            Remove(row);
            return (null, null, null);
        }
        group.OnSelectionChanged = null; // nothing of the workshop's
        for (int i = 0; i < buttons.Count; i++)
        {
            var label = buttons[i].GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label == null) continue;
            UiText.Set(label, $"x{Quantities[i]}");
            label.rectTransform.offsetMax = new Vector2(-12, label.rectTransform.offsetMax.y); // clear of the next dot
        }
        row.SetActive(false);
        return (row, group, buttons.ToArray());
    }

    /// <summary>Stretch a child across its parent's width (24 px margins), at its top (anchorY 1) or bottom (0).</summary>
    private static void Place(Transform t, float anchorY, Vector2 position, float height, float? pivotY = null)
    {
        var rt = t != null ? t.GetComponent<RectTransform>() : null;
        if (rt == null) return;
        rt.anchorMin = new Vector2(0, anchorY);
        rt.anchorMax = new Vector2(1, anchorY);
        rt.pivot = new Vector2(anchorY == 0 ? 0.5f : 0, pivotY ?? anchorY);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(-48, height);
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
