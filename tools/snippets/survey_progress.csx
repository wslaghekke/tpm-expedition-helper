// Survey level/progress of the locations with running expeditions, and the approach used.
var sb = new System.Text.StringBuilder();
foreach (var kv in TPS.Game.ExpeditionManager.Instance._expeditions)
{
    var e = kv.Value;
    TPS.Game.WorldMap.Instance.GetWorldMapLocationPOI(e.POI, out var poi);
    var xp = poi.SurveyXP;
    sb.AppendLine($"{e.POI.Name.Translation}: survey level {xp.Level}/{xp.MaxLevel} progress {xp.Progress:P0} (xp {xp.XP}); approach {e.Approach}");
}
sb.AppendLine("approaches: " + string.Join(", ", Enum.GetNames(typeof(TPS.Game.EExpeditionApproach))));
return sb.ToString();
