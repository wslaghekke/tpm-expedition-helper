using System.Collections.Generic;
using System.Linq;
using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Science materials an expedition can bring back (Metal, Whirlicogs, Golden Truffles, ...), the
/// player's stock of each (ScienceMaterialManager), and stock targets to offer for repeating.
/// </summary>
internal static class MaterialRewards
{
    public sealed class Option
    {
        public long Id;
        public string Name;
        public int Stock;
        public int Step; // a round number near the least one trip brings back
    }

    /// <summary>The materials this location can drop with the plan's cargo.</summary>
    public static List<Option> For(ExpeditionPlan plan)
    {
        var options = new List<Option>();
        var def = plan.PoiDefinition;
        if (def == null) return options;
        bool alternate = plan.Cargo != ExpeditionBonusModuleID.Null && ExpeditionManager.Instance.UseAlternatePOIData(plan.Cargo);
        foreach (var id in def.GetScienceMaterialRewards(alternate).Iter())
        {
            if (options.Any(o => o.Id == id.ID)) continue;
            options.Add(new Option { Id = id.ID, Name = Name(id.ID), Stock = Stock(id.ID), Step = Step(id.ID) });
        }
        return options;
    }

    public static int Stock(long id)
    {
        var manager = TPS.Core.StaticGameSystem<ScienceMaterialManager>.Instance;
        if (manager == null) return 0;
        manager.GetAmount(new ScienceMaterialID(id), out int amount);
        return amount;
    }

    public static string Name(long id)
    {
        var material = LevelDatabaseUtils.Get(new ScienceMaterialID(id));
        if (material == null) return $"material {id}";
        try
        {
            string shown = material.DisplayName;
            if (!string.IsNullOrEmpty(shown)) return shown;
        }
        catch { /* fall back to the asset name */ }
        return material.name;
    }

    /// <summary>
    /// Trips bring back very different amounts (1 Golden Truffle, 85+ Metal), so targets go in steps
    /// of the least a trip brings, rounded up to 1/2/5 × 10^n: 1, 5, 50, 100, ...
    /// </summary>
    public static int Step(long id)
    {
        var material = LevelDatabaseUtils.Get(new ScienceMaterialID(id));
        var perQuality = material?._amountPerQuality;
        int least = perQuality == null || perQuality.Length == 0 ? 1 : perQuality.Min();
        for (int scale = 1; ; scale *= 10)
            foreach (int n in new[] { 1, 2, 5 })
                if (n * scale >= least) return n * scale;
    }

    private static readonly int[] TargetSteps = { 1, 2, 3, 5, 10, 20, 50, 100, 200, 500, 1000 };

    /// <summary>The next few round targets above the current stock, e.g. 200, 300, 500, 1000 Metal.</summary>
    public static List<int> Targets(Option option, int count = 6)
    {
        var targets = TargetSteps.Select(n => n * option.Step).Where(t => t > option.Stock).Take(count).ToList();
        // A huge stock: carry on in steps above it.
        for (int t = (option.Stock / option.Step + 1) * option.Step; targets.Count < count; t += option.Step)
            if (!targets.Contains(t)) targets.Add(t);
        targets.Sort();
        return targets;
    }
}
