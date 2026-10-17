using System.Collections.Generic;
using System.Linq;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Whether to repeat expeditions to a location: the player's choice on the setup screen, or the
/// global default from the settings; and the goal picked instead of the exhibit goals, if any:
/// a perk (once, or until the player has a number of it), or a stock target for a material (one or the other);
/// and for art studio locations, how many sessions to run in a row.
/// Stored per museum by <see cref="MuseumData"/>.
/// </summary>
internal static class RepeatChoices
{
    private static readonly Dictionary<long, bool> ByPoi = new();
    private static readonly Dictionary<long, (long Perk, int Amount, int Batch)> PerkByPoi = new();
    private static readonly Dictionary<long, (long Material, int Amount, int Batch)> StockByPoi = new();
    private static readonly Dictionary<long, int> StudioRunsByPoi = new();

    public static bool IsOn(Settings settings, long poi) =>
        ByPoi.TryGetValue(poi, out bool on) ? on : settings.RepeatExpeditions.Value;

    /// <summary>
    /// The player's choice for a location. Only a choice that differs from the global default is
    /// kept: back at the default, the location follows the default again (also when it changes later).
    /// </summary>
    public static void Set(Settings settings, long poi, bool on)
    {
        if (on == settings.RepeatExpeditions.Value)
        {
            if (ByPoi.Remove(poi)) MuseumData.Changed();
            return;
        }
        if (ByPoi.TryGetValue(poi, out bool was) && was == on) return;
        ByPoi[poi] = on;
        MuseumData.Changed();
    }

    /// <summary>
    /// The perk (definition id) to repeat for at this location, or 0 for the exhibit goals; how many
    /// of it the player wants to have (Amount 0: until a crate brings it back once); and the "xN"
    /// picked on the setup screen to get there (Amount is what they had then + Batch).
    /// </summary>
    public static (long Perk, int Amount, int Batch) PerkGoal(long poi) => PerkByPoi.TryGetValue(poi, out var goal) ? goal : (0, 0, 0);

    public static long Perk(long poi) => PerkGoal(poi).Perk;

    public static void SetPerk(long poi, long perk, int amount = 0, int batch = 0)
    {
        if (perk == 0 || amount <= 0) amount = batch = 0;
        if (PerkGoal(poi) == (perk, amount, batch)) return;
        if (perk == 0) PerkByPoi.Remove(poi);
        else PerkByPoi[poi] = (perk, amount, batch);
        MuseumData.Changed();
    }

    /// <summary>The material and amount to repeat for at this location; Material 0 for none.</summary>
    public static (long Material, int Amount) Stock(long poi) =>
        StockByPoi.TryGetValue(poi, out var stock) ? (stock.Material, stock.Amount) : (0, 0);

    /// <summary>The "xN" picked on the setup screen for the stock target (0 if it predates the buttons).</summary>
    public static int StockBatch(long poi) => StockByPoi.TryGetValue(poi, out var stock) ? stock.Batch : 0;

    public static void SetStock(long poi, long material, int amount, int batch = 0)
    {
        if (material == 0 || amount <= 0) material = amount = batch = 0;
        if (Stock(poi) == (material, amount) && StockBatch(poi) == batch) return;
        if (material == 0) StockByPoi.Remove(poi);
        else StockByPoi[poi] = (material, amount, batch);
        MuseumData.Changed();
    }

    /// <summary>Art studio: how many sessions in a row to run, counting the one sent from the setup screen; 1 is no repeat.</summary>
    public static int StudioRuns(long poi) => StudioRunsByPoi.TryGetValue(poi, out int n) ? n : 1;

    public static void SetStudioRuns(long poi, int runs)
    {
        if (runs < 1) runs = 1;
        if (StudioRuns(poi) == runs) return;
        if (runs == 1) StudioRunsByPoi.Remove(poi);
        else StudioRunsByPoi[poi] = runs;
        MuseumData.Changed();
    }

    public static void Clear()
    {
        ByPoi.Clear();
        PerkByPoi.Clear();
        StockByPoi.Clear();
        StudioRunsByPoi.Clear();
    }

    public static IEnumerable<string> Save() =>
        ByPoi.Where(kv => kv.Value != Entry.Settings.RepeatExpeditions.Value) // the default may have changed since
            .Select(kv => $"repeat {kv.Key} {(kv.Value ? "on" : "off")}")
            .Concat(PerkByPoi.Select(kv => kv.Value.Amount > 0
                ? $"perk {kv.Key} {kv.Value.Perk} {kv.Value.Amount} {kv.Value.Batch}"
                : $"perk {kv.Key} {kv.Value.Perk}"))
            .Concat(StockByPoi.Select(kv => $"stock {kv.Key} {kv.Value.Material} {kv.Value.Amount} {kv.Value.Batch}"))
            .Concat(StudioRunsByPoi.Select(kv => $"studio {kv.Key} {kv.Value}"));

    /// <summary>Take one stored line (split on spaces) if it's ours.</summary>
    public static bool Load(string[] parts)
    {
        // "stock poi material amount [batch]": the batch came with the xN buttons.
        if (parts.Length is 4 or 5 && parts[0] == "stock" && long.TryParse(parts[1], out long stockPoi)
            && long.TryParse(parts[2], out long material) && int.TryParse(parts[3], out int amount) && material != 0 && amount > 0)
        {
            int stockBatch = parts.Length == 5 && int.TryParse(parts[4], out int b) ? b : 0;
            StockByPoi[stockPoi] = (material, amount, stockBatch);
            return true;
        }
        if (parts.Length == 5 && parts[0] == "perk" && long.TryParse(parts[1], out long perkPoi) && long.TryParse(parts[2], out long countedPerk)
            && int.TryParse(parts[3], out int perkAmount) && int.TryParse(parts[4], out int batch) && countedPerk != 0 && perkAmount > 0)
        {
            PerkByPoi[perkPoi] = (countedPerk, perkAmount, batch);
            return true;
        }
        if (parts.Length != 3 || !long.TryParse(parts[1], out long poi)) return false;
        if (parts[0] == "repeat" && parts[2] is "on" or "off")
        {
            bool on = parts[2] == "on";
            if (on != Entry.Settings.RepeatExpeditions.Value) ByPoi[poi] = on; // same as the default: follow it
            return true;
        }
        if (parts[0] == "perk" && long.TryParse(parts[2], out long perk) && perk != 0) { PerkByPoi[poi] = (perk, 0, 0); return true; }
        if (parts[0] == "studio" && int.TryParse(parts[2], out int runs) && runs > 1) { StudioRunsByPoi[poi] = runs; return true; }
        return false;
    }
}
