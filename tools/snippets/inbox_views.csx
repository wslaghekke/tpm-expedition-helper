// Read-only: which InboxMenu_Message view each inbox type's prefab uses (loads prefab assets, no instantiation).
var sb = new System.Text.StringBuilder();
foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.InboxMessageType>())
{
    try
    {
        var handle = t.MessagePrefab.LoadAssetAsync<UnityEngine.GameObject>();
        var go = handle.WaitForCompletion();
        var view = go?.GetComponent<TPS.Game.UI.InboxMenu_Message>();
        sb.AppendLine($"{t.name,-50} view={(view == null ? "?" : view.GetIl2CppType().Name)} buttons={view?._buttons?.Length}");
    }
    catch (Exception e) { sb.AppendLine($"{t.name}: {e.GetType().Name} {e.Message}"); }
}
return sb.ToString();
