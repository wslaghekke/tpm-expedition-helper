// Which exhibit qualities can drop at each survey level, per rarity.
var cfg = TPS.Game.WorldMap.Instance.Config;
var sb = new System.Text.StringBuilder();
for (int level = 0; level <= 6; level++)
{
    TPS.Game.ExhibitQualityConfig q;
    try { q = cfg.GetExhibitQualityConfig(level); } catch (Exception e) { sb.AppendLine($"level {level}: {e.GetType().Name}"); continue; }
    if (q == null) { sb.AppendLine($"level {level}: null"); continue; }
    sb.Append($"level {level}:");
    foreach (var ri in q.RarityInfos)
    {
        sb.Append($"  {ri.Rarity}(w{ri.Weight:0.##})[");
        foreach (var qi in ri.QualityInfos) sb.Append($"{qi.Quality}:{qi.Weight:0.##} ");
        sb.Append("]");
    }
    sb.AppendLine();
}
return sb.ToString();
