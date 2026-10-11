// Normal vs alternate exhibit rewards of the Dredge, and which cargo triggers the alternate table.
IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src)
{
    var en = src.GetEnumerator();
    var ne = en.Cast<Il2CppSystem.Collections.IEnumerator>();
    while (ne.MoveNext()) yield return en.Current;
}
var sb = new System.Text.StringBuilder();
var em = TPS.Game.ExpeditionManager.Instance;
sb.AppendLine("AlternatePOICargoTriggers: " + string.Join(", ", em.Config.AlternatePOICargoTriggers.Select(id => TPS.Game.LevelDatabaseUtils.Get(id)?.name ?? id.ToString())));
foreach (var poi in Iter(TPS.Game.WorldMap.Instance.AllPOIs))
{
    var d = poi.Definition;
    if (!d.name.Contains("Dredge")) continue;
    sb.AppendLine($"== {d.name}");
    sb.AppendLine("  normal:    " + string.Join(", ", Iter(d.GetExhibitRewards(false)).Select(id => TPS.Game.LevelDatabaseUtils.Get(id)?.name)));
    sb.AppendLine("  alternate: " + string.Join(", ", Iter(d.GetExhibitRewards(true)).Select(id => TPS.Game.LevelDatabaseUtils.Get(id)?.name)));
}
return sb.ToString();
