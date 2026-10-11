// Read-only: the Settings menu's tab buttons and panels.
var menu = UnityEngine.Object.FindObjectOfType<TPS.Game.UI.PreferencesMenu>();
if (menu == null) return "Settings not open";
var sb = new System.Text.StringBuilder();
string Path(UnityEngine.Transform t) { var s = t.name; while (t.parent != null && t != menu.transform) { t = t.parent; s = t.name + "/" + s; } return s; }
foreach (var tab in new TPS.Core.UI.UITab[] { menu._buttonGame, menu._buttonVideo, menu._buttonAudio, menu._buttonControls, menu._buttonStreaming })
{
    var comps = string.Join(",", tab.GetComponents<UnityEngine.Component>().Select(c => c.GetIl2CppType().Name));
    sb.AppendLine($"tab {Path(tab.transform)} active={tab.gameObject.activeSelf} [{comps}] children={tab.transform.childCount}");
}
foreach (var p in new UnityEngine.Component[] { menu._panelGame, menu._panelVideo, menu._panelAudio, menu._panelStreaming })
    sb.AppendLine($"panel {Path(p.transform)} active={p.gameObject.activeSelf}");
sb.AppendLine($"mode={menu._mode} available={string.Join(",", menu._availableModes.Select(m => m.ToString()))}");
return sb.ToString();
