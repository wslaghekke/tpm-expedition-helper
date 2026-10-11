// What's in each waiting crate, as the auto-claim feature sees it.
var em = TPS.Game.ExpeditionManager.Instance;
if (em == null) return "not in a level";
var sb = new System.Text.StringBuilder();
foreach (var kv in em._expeditions)
{
    var e = kv.Value;
    if (e.Stage != TPS.Game.EExpeditionStage.Reward) continue;
    var r = e.Rewards.Count > 0 ? e.Rewards[0] : null;
    var ex = r?.TryCast<TPS.Game.ExpeditionReward_Exhibit.Instance>();
    sb.AppendLine(ex == null
        ? $"{e.POI?.name}: {r?.DisplayName} [{r?.Cast<Il2CppSystem.Object>().GetIl2CppType().Name}] toInv={r?.CanMoveToInventory}"
        : $"{e.POI?.name}: exhibit {ex.DisplayName} quality={ex.ExhibitRecord.ExhibitQuality} new={ex.IsNewExhibit} toInv={ex.CanMoveToInventory}");
}
return sb.Length == 0 ? "no crates waiting" : sb.ToString();
