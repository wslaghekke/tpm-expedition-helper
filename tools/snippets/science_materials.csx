// Read-only: science material stock, and which locations drop which materials (weight of total).
IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src)
{
    var en = src.GetEnumerator();
    var ne = en.Cast<Il2CppSystem.Collections.IEnumerator>();
    while (ne.MoveNext()) yield return en.Current;
}
var sb = new System.Text.StringBuilder();
var mgr = TPS.Core.StaticGameSystem<TPS.Game.ScienceMaterialManager>.Instance;
sb.AppendLine("stock:");
foreach (var kv in mgr.Materials)
{
    mgr.GetAmount(kv.Key, out int amount);
    var def = TPS.Game.LevelDatabaseUtils.Get(kv.Key);
    string name; try { name = def.DisplayName; } catch { name = def?.name; }
    sb.AppendLine($"  {name} [{def?.name}] = {amount}");
}
foreach (var poi in Iter(TPS.Game.WorldMap.Instance.AllPOIs))
{
    var table = poi.Definition?.DropTable;
    if (table == null) continue;
    var lines = new List<string>();
    foreach (var (label, rewards) in new[] { ("normal", table.Rewards), ("alternate", table.AlternateRewards) })
    {
        if (rewards == null) continue;
        float total = 0;
        foreach (var r in rewards) if (r != null) total += r.Weight;
        foreach (var r in rewards)
        {
            var m = r?.TryCast<TPS.Game.ExpeditionReward_ScienceMaterial>();
            if (m == null) continue;
            var def = TPS.Game.LevelDatabaseUtils.Get(m.Material);
            lines.Add($"  {label}: {def?.name} weight {m.Weight} of {total}");
        }
    }
    if (lines.Count == 0) continue;
    string pname; try { pname = poi.Definition.Name.Translation; } catch { pname = poi.Definition.name; }
    sb.AppendLine($"{pname} ({poi.Definition.name})");
    foreach (var l in lines) sb.AppendLine(l);
}
return sb.ToString();
