// Read-only: hierarchy of the open expedition setup screen (ExpeditionMenu_Setup_V2).
var menu = UnityEngine.Object.FindObjectOfType<TPS.Game.UI.ExpeditionMenu_Setup_V2>();
if (menu == null) return "setup screen not open";
var sb = new System.Text.StringBuilder();
void Walk(UnityEngine.Transform t, int depth, int max)
{
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<UnityEngine.Component>()) { string n = c.GetIl2CppType().Name; if (n != "RectTransform" && n != "CanvasRenderer") comps.Add(n); }
    var tmp = t.GetComponent<TMPro.TMP_Text>();
    string extra = tmp != null ? $" \"{tmp.text?.Replace("\n"," ")}\"" : "";
    sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")} [{string.Join(",", comps)}]{extra}");
    if (depth >= max) return;
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, max);
}
Walk(menu.transform, 0, 7);
return sb.ToString();
