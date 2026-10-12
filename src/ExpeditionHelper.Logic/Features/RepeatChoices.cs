using System.Collections.Generic;
using System.Linq;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Whether to repeat expeditions to a location: the player's choice on the setup screen, or the
/// global default from the settings; and the goal picked instead of the exhibit goals, if any:
/// a perk, or a stock target for a material (one or the other).
/// Stored per museum by <see cref="MuseumData"/>.
/// </summary>
internal static class RepeatChoices
{
    private static readonly Dictionary<long, bool> ByPoi = new();
    private static readonly Dictionary<long, long> PerkByPoi = new();
    private static readonly Dictionary<long, (long Material, int Amount)> StockByPoi = new();

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

    /// <summary>The perk (definition id) to repeat for at this location, or 0 for the exhibit goals.</summary>
    public static long Perk(long poi) => PerkByPoi.TryGetValue(poi, out long perk) ? perk : 0;

    public static void SetPerk(long poi, long perk)
    {
        if (Perk(poi) == perk) return;
        if (perk == 0) PerkByPoi.Remove(poi);
        else PerkByPoi[poi] = perk;
        MuseumData.Changed();
    }

    /// <summary>The material and amount to repeat for at this location; Material 0 for none.</summary>
    public static (long Material, int Amount) Stock(long poi) => StockByPoi.TryGetValue(poi, out var stock) ? stock : (0, 0);

    public static void SetStock(long poi, long material, int amount)
    {
        if (Stock(poi) == (material, amount)) return;
        if (material == 0 || amount <= 0) StockByPoi.Remove(poi);
        else StockByPoi[poi] = (material, amount);
        MuseumData.Changed();
    }

    public static void Clear()
    {
        ByPoi.Clear();
        PerkByPoi.Clear();
        StockByPoi.Clear();
    }

    public static IEnumerable<string> Save() =>
        ByPoi.Where(kv => kv.Value != Entry.Settings.RepeatExpeditions.Value) // the default may have changed since
            .Select(kv => $"repeat {kv.Key} {(kv.Value ? "on" : "off")}")
            .Concat(PerkByPoi.Select(kv => $"perk {kv.Key} {kv.Value}"))
            .Concat(StockByPoi.Select(kv => $"stock {kv.Key} {kv.Value.Material} {kv.Value.Amount}"));

    /// <summary>Take one stored line (split on spaces) if it's ours.</summary>
    public static bool Load(string[] parts)
    {
        if (parts.Length == 4 && parts[0] == "stock" && long.TryParse(parts[1], out long stockPoi)
            && long.TryParse(parts[2], out long material) && int.TryParse(parts[3], out int amount) && material != 0 && amount > 0)
        {
            StockByPoi[stockPoi] = (material, amount);
            return true;
        }
        if (parts.Length != 3 || !long.TryParse(parts[1], out long poi)) return false;
        if (parts[0] == "repeat" && parts[2] is "on" or "off")
        {
            bool on = parts[2] == "on";
            if (on != Entry.Settings.RepeatExpeditions.Value) ByPoi[poi] = on; // same as the default: follow it
            return true;
        }
        if (parts[0] == "perk" && long.TryParse(parts[2], out long perk) && perk != 0) { PerkByPoi[poi] = perk; return true; }
        return false;
    }
}
