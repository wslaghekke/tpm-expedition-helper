// What the inventory holds, in each of its collections.
var inv = TPS.Game.Inventory.Instance;
var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
var sb = new System.Text.StringBuilder();
sb.AppendLine($"Items={inv.Items.Count} ReturnedEntities={inv.ReturnedEntities.Count} ReturnedExhibitsByType={inv.ReturnedExhibitsByType.Count}");
foreach (var it in inv.Items)
    sb.AppendLine($"  item: {it.Cast<Il2CppSystem.Object>().GetIl2CppType().Name}");
foreach (var e in inv.ReturnedEntities)
{
    if (em.HasComponent<TPS.Game.ECExhibit>(e)) { var c = em.GetComponentData<TPS.Game.ECExhibit>(e); sb.AppendLine($"  returned exhibit: {TPS.Game.LevelDatabaseUtils.Get(c.ExhibitDefinitionID)?.name} {c.ExhibitQuality}"); }
    else sb.AppendLine($"  returned entity {e} (no ECExhibit)");
}
return sb.ToString();
