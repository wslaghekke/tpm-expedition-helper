// Boolean/enum properties of the exhibit definition in each waiting crate (to see what drives CanMoveToInventory).
var em = TPS.Game.ExpeditionManager.Instance;
var sb = new System.Text.StringBuilder();
foreach (var kv in em._expeditions)
{
    var e = kv.Value;
    if (e.Stage != TPS.Game.EExpeditionStage.Reward) continue;
    var ex = e.Rewards[0].TryCast<TPS.Game.ExpeditionReward_Exhibit.Instance>();
    if (ex == null) continue;
    var def = ex.ExhibitRecord.Definition;
    sb.AppendLine($"== {ex.DisplayName} ({def.GetIl2CppType().FullName}) toInv={ex.CanMoveToInventory}");
    for (var t = def.GetType(); t != null && t.Namespace?.StartsWith("TPS") == true; t = t.BaseType)
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (!(p.PropertyType == typeof(bool) || p.PropertyType.IsEnum)) continue;
            object v; try { v = p.GetValue(def); } catch { continue; }
            sb.AppendLine($"  {t.Name}.{p.Name} = {v}");
        }
}
return sb.ToString();
