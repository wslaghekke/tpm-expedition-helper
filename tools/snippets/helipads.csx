// Read-only: every expedition helipad in the loaded museum (entities with ECExpeditionItem):
// helicopter state and animation time, and the expedition using it, if any.
var sb = new System.Text.StringBuilder();
var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
var query = em.CreateEntityQuery(new Unity.Entities.ComponentType[] { Unity.Entities.ComponentType.ReadOnly<TPS.Game.ECExpeditionItem>() });
var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
var manager = TPS.Game.ExpeditionManager.Instance;
for (int i = 0; i < entities.Length; i++)
{
    var c = em.GetComponentData<TPS.Game.ECExpeditionItem>(entities[i]);
    string exp = manager.TryGetExpeditionForItem(c.Item, out var e)
        ? $"{e.POI?.name} stage {e.Stage}" : "no expedition";
    sb.AppendLine($"helipad {c.Item.ID}: state={c.State} anim={c.AnimationTime:F1} — {exp}");
}
entities.Dispose();
foreach (var kv in manager._expeditions)
    sb.AppendLine($"expedition {kv.Value.POI?.name}: stage {kv.Value.Stage}, helipad {kv.Value.ItemID.ID}");
return sb.ToString();
