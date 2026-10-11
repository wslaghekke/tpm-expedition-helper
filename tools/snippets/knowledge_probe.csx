// Verify the knowledge model: thresholds, insight per quality, and ratings via the game's Get().
var kr = TPS.Game.ExhibitKnowledgeRatings.Instance;
var ar = TPS.Game.AnalysisRoomManager.Instance;
var sb = new System.Text.StringBuilder();
sb.AppendLine("KnowledgeInsightLevels = " + string.Join(",", kr._config.KnowledgeInsightLevels));
sb.AppendLine("ExhibitQualityLevelInsight = " + string.Join(",", ar.Config.ExhibitQualityLevelInsight));
var keys = kr.Ratings.GetKeyArray(Unity.Collections.Allocator.Temp);
sb.AppendLine($"Ratings count = {keys.Length}");
for (int i = 0; i < keys.Length && i < 15; i++)
{
    var id = keys[i];
    var r = kr.Get(default(Unity.Entities.Entity), id);
    sb.AppendLine($"  def={id} insight={r.Insight} knowledge={r.Knowledge} initial={r.InitialKnowledge} max={r.MaxKnowledge} calc(insight)={kr.CalculateKnowledgeLevel(r.Insight)}");
}
keys.Dispose();
return sb.ToString();
