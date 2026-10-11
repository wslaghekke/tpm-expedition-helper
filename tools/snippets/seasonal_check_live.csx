// For each running expedition: months it spans, and the seasonal event per month with its team's counter state.
var sb = new System.Text.StringBuilder();
sb.AppendLine($"now: month {TPS.Game.Timeline.Instance.Month}, day {TPS.Game.Timeline.Instance.Day}");
foreach (var kv in TPS.Game.ExpeditionManager.Instance._expeditions)
{
    var e = kv.Value;
    TPS.Game.WorldMap.Instance.GetWorldMapLocationPOI(e.POI, out var poi);
    var s = new TPS.Game.ExpeditionSettings(e.Location, poi, e.ItemID) { Approach = e.Approach, CargoModule = e.CargoModule, ExpeditionType = e.ExpeditionType };
    int i = 0;
    foreach (var slot in s.AssignedStaff) if (i < e.Staff.Length) slot.Staff = e.Staff[i++];
    s.GetDurationInMonths(out int start, out int end);
    sb.AppendLine($"{e.POI.name}: duration {s.GetDurationInDays()} days, months {start}->{end}, cargo {TPS.Game.LevelDatabaseUtils.Get(e.CargoModule)?.name}");
    for (int m = 0; m < 12; m++)
    {
        var se = s.GetSeasonalEvent(m);
        if (se?.SeasonalEvent?.Event == null) continue;
        sb.AppendLine($"  month {m}: {se.SeasonalEvent.Name.Translation} -> {se.SeasonalEvent.Event.GetActiveType(s)} (with this team+cargo)");
    }
}
return sb.ToString();
