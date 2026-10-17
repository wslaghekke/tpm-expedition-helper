// Is the workshop's x1/x3/x5/x10 quantity row (the template for the perk quantity row) in the scene?
// Run after a museum loads, before the workshop has been opened, and in a museum without a workshop.
string Path(UnityEngine.Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
var sb = new System.Text.StringBuilder();
var menu = UnityEngine.GameObject.Find("Canvas/LevelUI/HideableRoot/WorkshopStartProject");
sb.AppendLine($"GameObject.Find (active only): {(menu != null ? "found" : "not found")}");
// Find skips inactive objects: also look for the menu component itself, active or not.
var menus = UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.UI.WorkshopStartProjectMenu>().Where(m => m.gameObject.scene.IsValid()).ToList();
sb.AppendLine($"WorkshopStartProjectMenu in scene: {menus.Count}");
foreach (var m in menus)
{
    var row = m.GetComponentsInChildren<UnityEngine.Transform>(true).FirstOrDefault(t => t.name == "QuantityContainer");
    sb.AppendLine($"  {Path(m.transform)} active={m.gameObject.activeInHierarchy}");
    sb.AppendLine($"  QuantityContainer: {(row == null ? "missing" : $"found, buttons={row.GetComponent<TPS.Core.UI.UIButtonGroup>()?.Buttons?.Length}")}");
}
return sb.ToString();
