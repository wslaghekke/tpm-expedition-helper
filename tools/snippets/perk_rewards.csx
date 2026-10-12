// Every location that can drop perks: each perk with its survey level (UnlockLevel) and weight,
// for the normal and the alternate (cargo) drop table, plus the location's current survey level.
// Then the perks in any crate currently out or waiting. Read-only.
IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src)
{
    var en = src.GetEnumerator();
    var ne = en.Cast<Il2CppSystem.Collections.IEnumerator>();
    while (ne.MoveNext()) yield return en.Current;
}
string PerkName(TPS.Game.PerkDefinitionID id)
{
    var p = TPS.Game.LevelDatabaseUtils.Get(id);
    if (p == null) return $"perk {id.ID}";
    try { var n = p.DisplayName; if (!string.IsNullOrEmpty(n)) return $"{n} [{p.name}]"; } catch { }
    return p.name;
}
var sb = new System.Text.StringBuilder();
foreach (var poi in Iter(TPS.Game.WorldMap.Instance.AllPOIs))
{
    var d = poi.Definition;
    var table = d?.DropTable;
    if (table == null) continue;
    var lines = new List<string>();
    foreach (var (label, rewards) in new[] { ("normal", table.Rewards), ("alternate", table.AlternateRewards) })
    {
        if (rewards == null) continue;
        float total = 0;
        foreach (var r in rewards) if (r != null) total += r.Weight;
        foreach (var r in rewards)
        {
            var perk = r?.TryCast<TPS.Game.ExpeditionReward_Perk>();
            if (perk == null) continue;
            lines.Add($"  {label}: {PerkName(perk.Perk)} — level {perk.UnlockLevel}, weight {perk.Weight} of {total}");
        }
    }
    if (lines.Count == 0) continue;
    string name; try { name = d.Name.Translation; } catch { name = d.name; }
    sb.AppendLine($"{name} ({d.name}), survey level {poi.SurveyXP.Level}");
    foreach (var l in lines) sb.AppendLine(l);
}
foreach (var kv in TPS.Game.ExpeditionManager.Instance._expeditions)
{
    var e = kv.Value;
    var rewards = e.Rewards;
    var perks = new List<string>();
    if (rewards != null)
        for (int i = 0; i < rewards.Count; i++)
        {
            var p = rewards[i]?.TryCast<TPS.Game.ExpeditionReward_Perk.Instance>();
            if (p != null) perks.Add(PerkName(p.Perk));
        }
    sb.AppendLine($"expedition {e.POI?.name} stage {e.Stage}: {rewards?.Count ?? 0} reward(s), perks: {(perks.Count == 0 ? "none" : string.Join(", ", perks))}");
}
return sb.Length == 0 ? "no location drops perks" : sb.ToString();
