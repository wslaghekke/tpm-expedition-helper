using System.Collections.Generic;
using System.Linq;
using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Perks an expedition can bring back (science locations): the location's drop table lists them
/// as ExpeditionReward_Perk entries, each with the survey level it needs (UnlockLevel), and a
/// returned crate holds them as ExpeditionReward_Perk.Instance.
/// </summary>
internal static class PerkRewards
{
    public sealed class Option
    {
        public long Id;
        public string Name;
        public int UnlockLevel;
        public bool Available; // the location's survey level is high enough for it to drop
        public bool Discovered; // found at this location before; otherwise don't name it to the player
    }

    /// <summary>The perks this location can drop with the plan's cargo, in drop-table order.</summary>
    public static List<Option> For(ExpeditionPlan plan)
    {
        var options = new List<Option>();
        var table = plan.PoiDefinition?.DropTable;
        if (table == null) return options;
        bool alternate = plan.Cargo != ExpeditionBonusModuleID.Null && ExpeditionManager.Instance.UseAlternatePOIData(plan.Cargo);
        int surveyLevel = plan.PoiInstance?.SurveyXP.Level ?? 0;
        var discovered = TPS.Core.StaticGameSystem<DiscoveredPOIExhibits>.Instance; // what the setup screen's Stats tab shows

        // Unlock levels by perk, from both reward lists (GetPerkRewards only gives the ids).
        var unlockLevels = new Dictionary<long, int>();
        foreach (var rewards in new[] { table.Rewards, table.AlternateRewards })
        {
            if (rewards == null) continue;
            foreach (var reward in rewards)
            {
                var perk = reward?.TryCast<ExpeditionReward_Perk>();
                if (perk == null) continue;
                long id = perk.Perk.ID;
                unlockLevels[id] = unlockLevels.TryGetValue(id, out int level) ? System.Math.Min(level, perk.UnlockLevel) : perk.UnlockLevel;
            }
        }

        foreach (var id in table.GetPerkRewards(alternate).Iter())
        {
            if (options.Any(o => o.Id == id.ID)) continue;
            int unlock = unlockLevels.TryGetValue(id.ID, out int l) ? l : 0;
            options.Add(new Option
            {
                Id = id.ID, Name = Name(id.ID), UnlockLevel = unlock, Available = surveyLevel >= unlock,
                Discovered = discovered == null || discovered.IsDiscovered(plan.Poi, id),
            });
        }
        return options;
    }

    /// <summary>The perks in an expedition's crate (none until it has its rewards).</summary>
    public static IEnumerable<long> InCrate(Expedition expedition)
    {
        var rewards = expedition?.Rewards;
        if (rewards == null) yield break;
        for (int i = 0; i < rewards.Count; i++)
        {
            var perk = rewards[i]?.TryCast<ExpeditionReward_Perk.Instance>();
            if (perk != null) yield return perk.Perk.ID;
        }
    }

    /// <summary>
    /// The perk's name if the player has found it at this location, else "an undiscovered perk"
    /// (like exhibits: player-facing texts don't name what hasn't been discovered).
    /// </summary>
    public static string Shown(WorldMapLocationPOIDefinitionID poi, long id)
    {
        var discovered = TPS.Core.StaticGameSystem<DiscoveredPOIExhibits>.Instance;
        return discovered == null || discovered.IsDiscovered(poi, new PerkDefinitionID(id)) ? Name(id) : "an undiscovered perk";
    }

    /// <summary>The name the player sees, or the asset name.</summary>
    public static string Name(long id)
    {
        var perk = new PerkDefinitionID(id).Get();
        if (perk == null) return $"perk {id}";
        try
        {
            string shown = perk.DisplayName;
            if (!string.IsNullOrEmpty(shown)) return shown;
        }
        catch { /* fall back to the asset name */ }
        return perk.name;
    }
}
