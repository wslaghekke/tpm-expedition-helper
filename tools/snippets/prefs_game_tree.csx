// Read-only: hierarchy of the open Settings > Game panel (components per node, TMP text).
var panel = UnityEngine.Object.FindObjectOfType<TPS.Game.UI.PreferencesPanelGame>();
if (panel == null) return "Game panel not open";
var sb = new System.Text.StringBuilder();
void Walk(UnityEngine.Transform t, int depth)
{
    if (depth > 9) return;
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<UnityEngine.Component>())
    {
        string n = c.GetIl2CppType().Name;
        if (n == "RectTransform" || n == "CanvasRenderer") continue;
        comps.Add(n);
    }
    var tmp = t.GetComponent<TMPro.TMP_Text>();
    string text = tmp != null ? $" \"{tmp.text?.Replace("\n", " ")}\"" : "";
    sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")} [{string.Join(",", comps)}]{text}");
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
}
Walk(panel.transform, 0);
return sb.ToString();
