using System;
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
        public bool Discovered; // found at this location before; otherwise don't name it to the player
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
        var discovered = TPS.Core.StaticGameSystem<DiscoveredPOIExhibits>.Instance; // what the setup screen's Stats tab shows
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
                // Analysing destroys the exhibit: if the only pristine copy is in the inventory,
                // keep it out of the knowledge count so it can't satisfy both goals.
                bool pristinePlaced = copies.Any(c => !c.Item2 && c.Item1 == ExhibitQuality.Pristine);
                bool pristineInInventory = copies.Any(c => c.Item2 && c.Item1 == ExhibitQuality.Pristine);
                if (!exhibit.ShouldIgnoreQuality && !pristinePlaced && pristineInInventory) analysable--;
                if (analysable > 0)
                    potential = rating.InitialKnowledge + knowledge.CalculateKnowledgeLevel(rating.Insight + analysable);
            }
            goals.Exhibits.Add(new ExhibitStatus
            {
                Name = DisplayName(exhibit),
                Discovered = discovered == null || discovered.IsDiscovered(plan.Poi, id),
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

        // Exhibits claimed from expeditions (and ones taken back from the museum) are stored in the
        // inventory as real entities, not as Items records.
        foreach (var e in Inventory.Instance.ReturnedEntities)
        {
            if (!entityManager.HasComponent<ECExhibit>(e)) continue;
            var c = entityManager.GetComponentData<ECExhibit>(e);
            Add(c.ExhibitDefinitionID.ID, c.ExhibitQuality, true);
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

    // Player-facing texts name only discovered exhibits; the rest are counted ("2 undiscovered"),
    // like the setup screen's Stats tab. (Describe, for the log, names everything.)
    private List<string> MissingPristine() =>
        Named(Exhibits.Where(e => !e.PristineOwned), e => e.Name);

    private List<string> MissingKnowledge() =>
        Named(Exhibits.Where(e => e.PotentialKnowledge < e.MaxKnowledge), e => $"{e.Name} ({e.PotentialKnowledge}/{e.MaxKnowledge})");

    private static List<string> Named(IEnumerable<ExhibitStatus> exhibits, Func<ExhibitStatus, string> format)
    {
        var list = exhibits.ToList();
        var names = list.Where(e => e.Discovered).Select(format).ToList();
        int hidden = list.Count(e => !e.Discovered);
        if (hidden > 0) names.Add($"{hidden} undiscovered");
        return names;
    }

    /// <summary>What's still missing, e.g. "pristine: Ol' Smack-a-Vole; knowledge: Shell Phone (2/3), 1 undiscovered".</summary>
    public string StillNeeded(bool pristine = true, bool knowledge = true)
    {
        var parts = new List<string>();
        var p = MissingPristine();
        var k = MissingKnowledge();
        if (pristine && p.Count > 0) parts.Add("pristine: " + string.Join(", ", p));
        if (knowledge && k.Count > 0) parts.Add("knowledge: " + string.Join(", ", k));
        return string.Join("; ", parts);
    }

    /// <summary>What's missing as one line per goal: "Pristine: A, B" / "Knowledge: A (1/2), 1 undiscovered".</summary>
    public string MissingByGoal(bool pristine, bool knowledge)
    {
        var lines = new List<string>();
        var p = MissingPristine();
        var k = MissingKnowledge();
        if (pristine && p.Count > 0) lines.Add("Pristine: " + string.Join(", ", p));
        if (knowledge && k.Count > 0) lines.Add("Knowledge: " + string.Join(", ", k));
        return string.Join("\n", lines);
    }

    public string Describe() => string.Join(", ", Exhibits.Select(e =>
        $"{e.Name} [{(e.PristineOwned ? "pristine" : "no pristine")}, knowledge {e.Knowledge}" +
        (e.PotentialKnowledge > e.Knowledge ? $"(+{e.PotentialKnowledge - e.Knowledge} from inventory)" : "") + $"/{e.MaxKnowledge}]"));
}
