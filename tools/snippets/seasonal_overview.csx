// Seasonal events per month for each current expedition's location, and whether its team counters them.
var sb = new System.Text.StringBuilder();
foreach (var kv in TPS.Game.ExpeditionManager.Instance._expeditions)
{
    var e = kv.Value;
    TPS.Game.WorldMap.Instance.GetWorldMapLocationPOI(e.POI, out var poi);
    var s = new TPS.Game.ExpeditionSettings(e.Location, poi, e.ItemID);
    sb.AppendLine($"{e.POI.name}:");
    for (int m = 0; m < 12; m++)
    {
        var se = s.GetSeasonalEvent(m);
        if (se?.SeasonalEvent?.Event == null) continue;
        sb.AppendLine($"  month {m}: {se.SeasonalEvent.Name.Translation} ({se.Start}-{se.End}) active(no team)={se.SeasonalEvent.Event.GetActiveType(s)}");
    }
}
return sb.ToString();
