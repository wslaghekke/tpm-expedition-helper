// Every ECS entity with ECExhibit for the given exhibits: quality, and whether GetPlacedExhibits / inventory know it.
var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
var query = em.CreateEntityQuery(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Unity.Entities.ComponentType>(new[] { Unity.Entities.ComponentType.ReadOnly<TPS.Game.ECExhibit>() }));
var all = query.ToEntityArray(Unity.Collections.Allocator.Temp);
var placed = new Il2CppSystem.Collections.Generic.List<Unity.Entities.Entity>();
TPS.Game.ExhibitUtils.GetPlacedExhibits(ref placed);
var placedSet = new HashSet<int>(); foreach (var e in placed) placedSet.Add(e.Index);
var returnedSet = new HashSet<int>(); foreach (var e in TPS.Game.Inventory.Instance.ReturnedEntities) returnedSet.Add(e.Index);
var sb = new System.Text.StringBuilder();
sb.AppendLine($"entities with ECExhibit: {all.Length}, placed list: {placed.Count}, returned: {returnedSet.Count}");
for (int i = 0; i < all.Length; i++)
{
    var c = em.GetComponentData<TPS.Game.ECExhibit>(all[i]);
    var name = TPS.Game.LevelDatabaseUtils.Get(c.ExhibitDefinitionID)?.name ?? "?";
    if (!(name.Contains("Starfish") || name.Contains("FlashyFish"))) continue;
    sb.AppendLine($"  {all[i]} {name} {c.ExhibitQuality} placed={placedSet.Contains(all[i].Index)} inventory={returnedSet.Contains(all[i].Index)}");
}
all.Dispose();
return sb.ToString();
