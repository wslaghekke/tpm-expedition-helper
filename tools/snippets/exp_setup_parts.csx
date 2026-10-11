// Read-only: the cargo section of the setup screen and the approach radio row of the expedition menu.
var setup = UnityEngine.Object.FindObjectOfType<TPS.Game.UI.ExpeditionMenu_Setup_V2>();
if (setup == null) return "setup screen not open";
var sb = new System.Text.StringBuilder();
void Walk(UnityEngine.Transform t, int depth, int max)
{
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<UnityEngine.Component>()) { string n = c.GetIl2CppType().Name; if (n != "RectTransform" && n != "CanvasRenderer") comps.Add(n); }
    var tmp = t.GetComponent<TMPro.TMP_Text>();
    var rt = t.GetComponent<UnityEngine.RectTransform>();
    string extra = tmp != null ? $" \"{tmp.text?.Replace("\n"," ")}\"" : "";
    sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")} [{string.Join(",", comps)}] {rt?.rect.width:0}x{rt?.rect.height:0}{extra}");
    if (depth >= max) return;
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, max);
}
var content = setup._buttonAutoFill.transform.parent;
Walk(content.Find("containerCargo"), 0, 5);
Walk(setup._buttonAutoFill.transform, 0, 3);
// Approach row: find the text "Safe" anywhere under the expedition menu root.
var root = setup.transform.root;
foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
    if (tmp.text == "Safe") { var row = tmp.transform.parent.parent; sb.AppendLine($"approach row path: {row.parent?.name}/{row.name}"); Walk(row, 0, 4); break; }
return sb.ToString();
