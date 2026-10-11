// State of the expedition helipads (ECExpeditionItem on the item's entity).
var sb = new System.Text.StringBuilder();
foreach (long raw in new long[] { 37997, 38005 })
{
    var id = new TPS.Game.GameItemInstanceID(raw);
    if (!id.TryGet(out var item)) { sb.AppendLine($"{raw}: not found"); continue; }
    var entityProp = item.GetType().GetProperty("Entity");
    sb.AppendLine($"{raw}: {item.GetIl2CppType().Name} '{item.ToString()}' entityProp={(entityProp == null ? "none" : entityProp.PropertyType.FullName)}");
    if (entityProp == null) continue;
    var entity = (Unity.Entities.Entity)entityProp.GetValue(item);
    var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
    bool has = em.HasComponent<TPS.Game.ECExpeditionItem>(entity);
    sb.AppendLine($"   entity={entity} hasECExpeditionItem={has}");
    if (has) { var c = em.GetComponentData<TPS.Game.ECExpeditionItem>(entity); sb.AppendLine($"   state={c.State} anim={c.AnimationTime:F2} item={c.Item}"); }
}
return sb.ToString();
