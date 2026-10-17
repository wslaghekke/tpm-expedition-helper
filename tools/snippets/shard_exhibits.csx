// Multi-part (shard) exhibits: every ECExhibit entity whose definition IsShardExhibit, with quality,
// placed/inventory, installed piece count and completeness; then every POI that drops shard exhibits,
// with knowledge now/max (to see whether knowledge carries over between museums).
IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src)
{
    var en = src.GetEnumerator();
    var ne = en.Cast<Il2CppSystem.Collections.IEnumerator>();
    while (ne.MoveNext()) yield return en.Current;
}
var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
var sb = new System.Text.StringBuilder();

var query = em.CreateEntityQuery(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Unity.Entities.ComponentType>(new[] { Unity.Entities.ComponentType.ReadOnly<TPS.Game.ECExhibit>() }));
var all = query.ToEntityArray(Unity.Collections.Allocator.Temp);
var placed = new Il2CppSystem.Collections.Generic.List<Unity.Entities.Entity>();
TPS.Game.ExhibitUtils.GetPlacedExhibits(ref placed);
var placedSet = new HashSet<int>(); foreach (var e in placed) placedSet.Add(e.Index);
var returnedSet = new HashSet<int>(); foreach (var e in TPS.Game.Inventory.Instance.ReturnedEntities) returnedSet.Add(e.Index);
sb.AppendLine($"ECExhibit entities: {all.Length}");
for (int i = 0; i < all.Length; i++)
{
    var c = em.GetComponentData<TPS.Game.ECExhibit>(all[i]);
    var def = TPS.Game.LevelDatabaseUtils.Get(c.ExhibitDefinitionID);
    if (def == null || !def.IsShardExhibit) continue;
    string shards = "no buffer";
    if (em.HasBuffer<TPS.Game.EDBExhibitShard>(all[i]))
    {
        var buf = em.GetBuffer<TPS.Game.EDBExhibitShard>(all[i], true);
        shards = $"buffer={buf.Length} installs={TPS.Game.EDBExhibitShardUtils.NumberOfInstalls(buf)} complete={TPS.Game.EDBExhibitShardUtils.IsComplete(buf)}";
    }
    sb.AppendLine($"  {all[i]} {def.name} {c.ExhibitQuality} placed={placedSet.Contains(all[i].Index)} inventory={returnedSet.Contains(all[i].Index)} {shards}");
}
all.Dispose();

var kr = TPS.Game.ExhibitKnowledgeRatings.Instance;
foreach (var poi in Iter(TPS.Game.WorldMap.Instance.AllPOIs))
{
    var d = poi.Definition;
    var lines = new List<string>();
    foreach (var id in Iter(d.GetAllExhibitRewards()))
    {
        var def = TPS.Game.LevelDatabaseUtils.Get(id);
        if (def == null || !def.IsShardExhibit) continue;
        var r = kr.Get(default(Unity.Entities.Entity), id);
        lines.Add($"    {def.name} ignoreQuality={def.ShouldIgnoreQuality} unique={def.IsUnique} knowledge={r.Knowledge}/{r.MaxKnowledge} insight={r.Insight}");
    }
    if (lines.Count == 0) continue;
    sb.AppendLine($"== {d.name}");
    foreach (var l in lines) sb.AppendLine(l);
}
return sb.ToString();
