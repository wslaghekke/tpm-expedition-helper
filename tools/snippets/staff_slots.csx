// Debug: staff slots of a fresh ExpeditionSettings for each current expedition, and team fit.
var sb = new System.Text.StringBuilder();
foreach (var kv in TPS.Game.ExpeditionManager.Instance._expeditions)
{
    var e = kv.Value;
    TPS.Game.WorldMap.Instance.GetWorldMapLocationPOI(e.POI, out var poi);
    var s = new TPS.Game.ExpeditionSettings(e.Location, poi, e.ItemID);
    sb.AppendLine($"{e.POI.name}: slots={s.AssignedStaff.Length} team={e.Staff.Length}");
    int i = 0;
    foreach (var slot in s.AssignedStaff)
    {
        var r = slot.RequiredStaff;
        sb.Append($"  slot{i++}: type={r.Type} count={r.Count} minRank={r.MinimumRank} qual={r.Qualification.ID} assigned={slot.Staff.ID} fits:");
        foreach (var id in e.Staff) if (id.TryGet(out var st)) sb.Append($" {st.ToString().Split(':')[1].Trim()}={r.IsValidForExpedition(st)}");
        sb.AppendLine();
    }
}
return sb.ToString();
