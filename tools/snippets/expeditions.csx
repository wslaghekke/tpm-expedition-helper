// List live expeditions with stage, timing and rewards.
var em = TPS.Game.ExpeditionManager.Instance;
if (em == null) return "no ExpeditionManager (not in a level?)";
var sb = new System.Text.StringBuilder();
sb.AppendLine($"sinceNewExhibit={em._expeditionsSinceNewExhibit} sincePristine={em._expeditionsSincePristineExhibit} count={em._expeditions.Count}");
foreach (var kv in em._expeditions)
{
    var e = kv.Value;
    sb.Append($"- {e.POI?.name} stage={e.Stage} t={e.Time:F1}/{e.Duration:F1} cost={e.Cost} item={e.ItemID} staff={e.Staff?.Length} rewards=");
    if (e.Rewards != null) foreach (var r in e.Rewards) sb.Append($"[{r.DisplayName}, toInv={r.CanMoveToInventory}] ");
    sb.AppendLine();
}
return sb.ToString();
