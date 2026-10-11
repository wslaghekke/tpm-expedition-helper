// Read-only: Settings menu structure outside the panel contents, plus tab click wiring.
var menu = UnityEngine.Object.FindObjectOfType<TPS.Game.UI.PreferencesMenu>();
if (menu == null) return "Settings not open";
var sb = new System.Text.StringBuilder();
void Walk(UnityEngine.Transform t, int depth, int max)
{
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<UnityEngine.Component>()) { string n = c.GetIl2CppType().Name; if (n != "RectTransform" && n != "CanvasRenderer") comps.Add(n); }
    var tmp = t.GetComponent<TMPro.TMP_Text>();
    var img = t.GetComponent<UnityEngine.UI.Image>();
    string extra = tmp != null ? $" \"{tmp.text}\"" : (img != null && img.sprite != null ? $" sprite={img.sprite.name}" : "");
    sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")} [{string.Join(",", comps)}]{extra}");
    if (depth >= max || t.name == "Content" && depth > 2) return;
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, max);
}
Walk(menu.transform.Find("MenuRoot/ScreenRegionContainer/MenuContainer"), 0, 4);
var tab = menu._buttonStreaming;
var btn = tab._button;
sb.AppendLine($"tab5 button={(btn == null ? "null" : btn.name)} persistent onClick={btn?.onClick.GetPersistentEventCount()}");
return sb.ToString();
