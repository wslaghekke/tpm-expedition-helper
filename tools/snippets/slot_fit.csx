// Which staff fit the first staff slot of each current expedition's POI (is IsValidForExpedition availability-aware?).
var sb = new System.Text.StringBuilder();
foreach (var kv in TPS.Game.ExpeditionManager.Instance._expeditions)
{
    var e = kv.Value;
    TPS.Game.WorldMap.Instance.GetWorldMapLocationPOI(e.POI, out var poi);
    var s = new TPS.Game.ExpeditionSettings(e.Location, poi, e.ItemID);
    var r = s.AssignedStaff[0].RequiredStaff;
    var fits = new List<string>();
    foreach (var st in TPS.Game.StaffManager.Instance.Staff)
        if (r.IsValidForExpedition(st)) fits.Add(st.ToString().Split(':')[1].Trim());
    sb.AppendLine($"{e.POI.name}: {fits.Count} of {TPS.Game.StaffManager.Instance.Staff.Count} staff fit: {string.Join(", ", fits)}");
}
return sb.ToString();
