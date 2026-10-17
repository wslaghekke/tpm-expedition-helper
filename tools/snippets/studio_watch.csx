// Watch how an art studio session ends and how its piece is handed over: logs changes (only) to
// studio sessions (stage, reward), loaded scenes, studio reveal stations and the inventory's
// exhibit count, prefixed [studio-watch]. Checks every 10 frames; the reveal-station query is made
// once. Stop with: ((Action)Host.Exports["StudioWatch.Stop"])();   (a logic reload also clears it)
var log = Host.Log;
var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
var stationQuery = em.CreateEntityQuery(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Unity.Entities.ComponentType>(
    new[] { Unity.Entities.ComponentType.ReadOnly<TPS.Game.ECStudioRevealStation>() }));
var last = new Dictionary<string, string>();
void Note(string key, string value)
{
    if (last.TryGetValue(key, out var was) && was == value) return;
    last[key] = value;
    log.LogInfo($"[studio-watch] {key}: {value}");
}
string Reward(TPS.Game.Expedition e)
{
    var r = e.Rewards?.Count > 0 ? e.Rewards[0] : null;
    if (r == null) return "no reward";
    string s = $"{r.Cast<Il2CppSystem.Object>().GetIl2CppType().Name} '{r.DisplayName}' canInventory={r.CanMoveToInventory} button='{r.ClaimButtonText}'";
    var ex = r.TryCast<TPS.Game.ExpeditionReward_Exhibit.Instance>();
    if (ex != null) s += $" new={ex.IsNewExhibit} quality={ex.ExhibitRecord.ExhibitQuality}";
    return s;
}
var seen = new HashSet<string>();
Action tick = () =>
{
    if (UnityEngine.Time.frameCount % 10 != 0) return;
    var manager = TPS.Game.ExpeditionManager.Instance;
    if (manager == null) return;
    var now = new HashSet<string>();
    foreach (var kv in manager._expeditions)
    {
        var e = kv.Value;
        if (e.ExpeditionType == TPS.Game.EExpeditionType.Expedition) continue;
        string key = $"session {e.POI?.name} #{kv.Key}";
        now.Add(key);
        Note(key, $"stage {e.Stage}; {Reward(e)}");
    }
    foreach (var key in seen) if (!now.Contains(key)) { log.LogInfo($"[studio-watch] {key}: gone"); last.Remove(key); }
    seen = now;

    var scenes = new List<string>();
    for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) scenes.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
    Note("scenes", string.Join(", ", scenes));

    Note("reveal stations", stationQuery.CalculateEntityCount().ToString());

    int exhibits = 0;
    foreach (var it in TPS.Game.Inventory.Instance.Items) if (it.TryCast<TPS.Game.InventoryItem_Exhibit>() != null) exhibits++;
    Note("inventory", $"{TPS.Game.Inventory.Instance.ReturnedEntities.Count} returned entities, {exhibits} exhibit items");
};
Host.Update += tick;
Host.Exports["StudioWatch.Stop"] = (Action)(() => { Host.Update -= tick; log.LogInfo("[studio-watch] stopped"); });
return "watching";
