// For the POIs we use: every exhibit they can drop, owned copies (placed + inventory) by quality,
// pristine owned?, knowledge now/max, analysable inventory copies.
// Il2Cpp IEnumerable<T> wrappers have no MoveNext on the generic enumerator; go via the non-generic one.
IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src)
{
    var en = src.GetEnumerator();
    var ne = en.Cast<Il2CppSystem.Collections.IEnumerator>();
    while (ne.MoveNext()) yield return en.Current;
}
var wm = TPS.Game.WorldMap.Instance;
var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
var kr = TPS.Game.ExhibitKnowledgeRatings.Instance;

// Owned copies: definition id -> list of (quality, where)
var owned = new Dictionary<long, List<string>>();
void Own(long def, TPS.Game.ExhibitQuality q, string where) { if (!owned.TryGetValue(def, out var l)) owned[def] = l = new(); l.Add($"{q}@{where}"); }
var placed = new Il2CppSystem.Collections.Generic.List<Unity.Entities.Entity>();
TPS.Game.ExhibitUtils.GetPlacedExhibits(ref placed);
foreach (var e in placed)
    if (em.HasComponent<TPS.Game.ECExhibit>(e)) { var c = em.GetComponentData<TPS.Game.ECExhibit>(e); Own(c.ExhibitDefinitionID.ID, c.ExhibitQuality, "placed"); }
int invExhibits = 0;
foreach (var it in TPS.Game.Inventory.Instance.Items)
{
    var ie = it.TryCast<TPS.Game.InventoryItem_Exhibit>();
    if (ie == null) continue;
    invExhibits++;
    var rec = ie.ExhibitRecord;
    Own(((TPS.Game.ExhibitDefinitionID)rec.Definition).ID, rec.ExhibitQuality, "inv");
}
var sb = new System.Text.StringBuilder();
sb.AppendLine($"placed exhibits={placed.Count} inventory exhibits={invExhibits}");

foreach (var poi in Iter(wm.AllPOIs))
{
    var d = poi.Definition;
    if (!(d.name.Contains("SmallPrehistoryInventions"))) continue;
    sb.AppendLine($"== {d.name} surveyLevel={poi.SurveyXP.Level}");
    foreach (var id in Iter(d.GetAllExhibitRewards()))
    {
        var def = TPS.Game.LevelDatabaseUtils.Get(id);
        var copies = owned.TryGetValue(id.ID, out var l) ? l : new List<string>();
        bool pristine = copies.Any(s => s.StartsWith("Pristine"));
        var r = kr.Get(default(Unity.Entities.Entity), id);
        int invCopies = copies.Count(s => s.EndsWith("@inv"));
        sb.AppendLine($"  {def?.name,-45} shard={def?.IsShardExhibit} unique={def?.IsUnique} pristineOwned={pristine} knowledge={r.Knowledge}/{r.MaxKnowledge} invCopies={invCopies} copies=[{string.Join(" ", copies)}]");
    }
}
return sb.ToString();
