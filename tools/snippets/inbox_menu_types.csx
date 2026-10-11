// Read-only: the inbox types the inbox menu knows about.
var menus = UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.UI.InboxMenu>();
var sb = new System.Text.StringBuilder();
sb.AppendLine($"InboxMenu instances: {menus.Length}");
foreach (var m in menus)
{
    var types = m._messageTypes;
    sb.AppendLine($"  {m.name} messageTypes={(types == null ? "null" : types.Count.ToString())}");
    if (types == null) continue;
    foreach (var t in types) sb.AppendLine($"    {t?.name}");
}
return sb.ToString();
