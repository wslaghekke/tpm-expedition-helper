// Load the Generic and Expeditions inbox types by Addressables GUID (from the catalog); show their view, name and filter. No UI shown.
var sb = new System.Text.StringBuilder();
foreach (var path in new[] {
    "c162dd7665e2fe24480b4dc413e6b68d", // InboxMessageType_Expeditions
    "4a33c6568d26e604585b403c47351f28" }) // InboxMessageType_Generic
{
    try
    {
        var t = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<TPS.Game.InboxMessageType>(path).WaitForCompletion();
        if (t == null) { sb.AppendLine($"{path}: null"); continue; }
        var prefab = t.MessagePrefab.LoadAssetAsync<UnityEngine.GameObject>().WaitForCompletion();
        var view = prefab?.GetComponent<TPS.Game.UI.InboxMenu_Message>();
        sb.AppendLine($"{t.name}: display='{t.DisplayName.Translation}' filter={t.InboxMessageFilterType} view={(view == null ? "?" : view.GetIl2CppType().Name)} buttons={view?._buttons?.Length} prefab={prefab?.name}");
    }
    catch (Exception e) { sb.AppendLine($"{path}: {e.GetType().Name}: {e.Message}"); }
}
return sb.ToString();
