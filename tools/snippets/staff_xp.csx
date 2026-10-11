// Read-only: specialisation XP of the staff on current expeditions (and the expedition's crate, if any).
// Uses Qualifications.GetXP (a plain method) rather than the IXPLevelReadonly interface properties,
// which crashed the game when read reflectively.
var sb = new System.Text.StringBuilder();
var qProp = typeof(TPS.Game.StaffInstance).GetProperties().FirstOrDefault(p => p.PropertyType == typeof(TPS.Game.Qualifications));
foreach (var kv in TPS.Game.ExpeditionManager.Instance._expeditions)
{
    var e = kv.Value;
    sb.AppendLine($"{e.POI?.name} stage {e.Stage}");
    foreach (var id in e.Staff)
    {
        if (!id.TryGet(out var s)) continue;
        var q = (TPS.Game.Qualifications)qProp.GetValue(s);
        var spec = q.Specialisation;
        string xp = "?";
        if (spec != null)
        {
            // GetXP returns the IXPLevelReadonly interface: cast to the concrete class before reading.
            var o = q.GetXP(spec.Definition).Cast<Il2CppSystem.Object>();
            var sx = o.TryCast<TPS.Game.SpecialisationXPLevel>();
            xp = sx != null ? $"{sx.XP} (level {sx.Level})" : o.GetIl2CppType().FullName;
        }
        sb.AppendLine($"   {s.Name} rank {s.Rank} {spec?.Definition?.name}: xp {xp}");
    }
    if (e.Rewards != null)
        foreach (var r in e.Rewards)
        {
            var x = r.TryCast<TPS.Game.ExpeditionReward_ExpertXP.Instance>();
            sb.AppendLine("   reward: " + (x != null ? $"ExpertXP calculatedXP={x._calculatedXP} min={x._minXP} max={x._maxXP}" : r.Cast<Il2CppSystem.Object>().GetIl2CppType().Name));
        }
}
return sb.ToString();
