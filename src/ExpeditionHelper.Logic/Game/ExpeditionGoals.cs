using System.Collections.Generic;
using System.Linq;
using TPS.Game;
using Unity.Entities;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// The repeat goals, evaluated over every exhibit an expedition can drop with its cargo:
///  - pristine: the museum owns a pristine copy (placed or in inventory);
///  - knowledge: knowledge is maxed, optionally counting the insight from analysing inventory copies.
///
/// Model (verified in game): an analysis consumes the exhibit and gives 1 insight regardless of
/// quality; knowledge = InitialKnowledge + CalculateKnowledgeLevel(Insight), capped at MaxKnowledge.
/// </summary>
internal sealed class ExpeditionGoals
{
    // Not a record: records make the compiler emit NullableAttribute, which the interop'd
    // UnityEngine.CoreModule shadows (see README, build notes).
    public sealed class ExhibitStatus
    {
        public string Name;
        public bool PristineOwned;
        public int Knowledge, MaxKnowledge, PotentialKnowledge;
    }

    public List<ExhibitStatus> Exhibits { get; } = new();
    public bool PristinePossible { get; private set; }
    public bool UsesAlternateRewards { get; private set; }

    public bool AllPristine => Exhibits.All(e => e.PristineOwned);
    public bool AllKnowledge => Exhibits.All(e => e.PotentialKnowledge >= e.MaxKnowledge);

    public static ExpeditionGoals Evaluate(ExpeditionPlan plan, bool countInventory)
    {
        var goals = new ExpeditionGoals();
        var def = plan.PoiDefinition;
        var poi = plan.PoiInstance;
        if (def == null || poi == null) return goals;

        goals.UsesAlternateRewards = plan.Cargo != ExpeditionBonusModuleID.Null
                                     && ExpeditionManager.Instance.UseAlternatePOIData(plan.Cargo);
        goals.PristinePossible = ExpeditionUtils.GetMaxExhibitQuality(plan.Approach, poi.SurveyXP.Level) >= ExhibitQuality.Pristine;

        var owned = OwnedCopies();
        var knowledge = ExhibitKnowledgeRatings.Instance;
        foreach (var id in def.GetExhibitRewards(goals.UsesAlternateRewards).Iter())
        {
            var exhibit = LevelDatabaseUtils.Get(id);
            if (exhibit == null) continue;
            var copies = owned.TryGetValue(id.ID, out var list) ? list : new List<(ExhibitQuality, bool)>();

            bool pristine = exhibit.ShouldIgnoreQuality || copies.Any(c => c.Item1 == ExhibitQuality.Pristine);

            var rating = knowledge.Get(default(Entity), id);
            int potential = rating.Knowledge;
            if (countInventory && !exhibit.IsUnique)
            {
                int analysable = copies.Count(c => c.Item2); // inventory copies
                if (analysable > 0)
                    potential = rating.InitialKnowledge + knowledge.CalculateKnowledgeLevel(rating.Insight + analysable);
            }
            goals.Exhibits.Add(new ExhibitStatus
            {
                Name = DisplayName(exhibit),
                PristineOwned = pristine,
                Knowledge = rating.Knowledge,
                MaxKnowledge = rating.MaxKnowledge,
                PotentialKnowledge = System.Math.Min(potential, rating.MaxKnowledge),
            });
        }
        return goals;
    }

    /// <summary>The name the player sees ("Shell Phone"), not the asset name ("MI_Exhibit_Prehistory_BonePhone").</summary>
    public static string DisplayName(ExhibitDefinition exhibit)
    {
        try
        {
            string shown = exhibit.DisplayNameLocString.Translation;
            if (!string.IsNullOrEmpty(shown)) return shown;
        }
        catch { /* fall back to the asset name */ }
        return exhibit.name.Replace("MI_Exhibit_", "");
    }

    /// <summary>Definition id → (quality, inInventory) for every exhibit the museum owns.</summary>
    private static Dictionary<long, List<(ExhibitQuality, bool)>> OwnedCopies()
    {
        var owned = new Dictionary<long, List<(ExhibitQuality, bool)>>();
        void Add(long def, ExhibitQuality q, bool inInventory)
        {
            if (!owned.TryGetValue(def, out var l)) owned[def] = l = new List<(ExhibitQuality, bool)>();
            l.Add((q, inInventory));
        }

        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var placed = new Il2CppSystem.Collections.Generic.List<Entity>();
        ExhibitUtils.GetPlacedExhibits(ref placed);
        foreach (var e in placed)
        {
            if (!entityManager.HasComponent<ECExhibit>(e)) continue;
            var c = entityManager.GetComponentData<ECExhibit>(e);
            Add(c.ExhibitDefinitionID.ID, c.ExhibitQuality, false);
        }

        foreach (var item in Inventory.Instance.Items)
        {
            var exhibit = item.TryCast<InventoryItem_Exhibit>();
            if (exhibit == null) continue;
            var record = exhibit.ExhibitRecord;
            Add(((ExhibitDefinitionID)record.Definition).ID, record.ExhibitQuality, true);
        }
        return owned;
    }

    public string Describe() => string.Join(", ", Exhibits.Select(e =>
        $"{e.Name} [{(e.PristineOwned ? "pristine" : "no pristine")}, knowledge {e.Knowledge}" +
        (e.PotentialKnowledge > e.Knowledge ? $"(+{e.PotentialKnowledge - e.Knowledge} from inventory)" : "") + $"/{e.MaxKnowledge}]"));
}
