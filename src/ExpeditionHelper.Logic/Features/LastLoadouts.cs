using System.Collections.Generic;
using System.Linq;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// The last team and cargo sent to each location (whatever helipad it left from), for the
/// setup screen's "Last team" button. Filled from the expeditions <see cref="RepeatExpeditions"/>
/// sees while they're out, so it also covers expeditions the player sent by hand.
/// Stored per museum by <see cref="MuseumData"/>.
/// </summary>
internal static class LastLoadouts
{
    public sealed class Loadout
    {
        public List<StaffInstanceID> Staff = new();
        public ExpeditionBonusModuleID Cargo = ExpeditionBonusModuleID.Null;

        public ExpeditionBonusModule CargoModule =>
            Cargo == ExpeditionBonusModuleID.Null ? null : LevelDatabaseUtils.Get(Cargo);
    }

    private static readonly Dictionary<long, Loadout> ByPoi = new();

    public static void Remember(ExpeditionPlan plan)
    {
        if (plan.Staff.Count == 0) return;
        long poi = plan.Poi.ID;
        if (ByPoi.TryGetValue(poi, out var known) && known.Cargo.ID == plan.Cargo.ID
            && known.Staff.Select(s => s.ID).SequenceEqual(plan.Staff.Select(s => s.ID))) return;
        ByPoi[poi] = new Loadout { Staff = plan.Staff.ToList(), Cargo = plan.Cargo };
        MuseumData.Changed();
    }

    public static Loadout Get(long poi) => ByPoi.TryGetValue(poi, out var loadout) ? loadout : null;

    public static void Clear() => ByPoi.Clear();

    public static IEnumerable<string> Save() =>
        ByPoi.Select(kv => $"team {kv.Key} {(kv.Value.Cargo == ExpeditionBonusModuleID.Null ? 0 : kv.Value.Cargo.ID)} {string.Join(",", kv.Value.Staff.Select(s => s.ID))}");

    /// <summary>Take one stored line (split on spaces) if it's ours.</summary>
    public static bool Load(string[] parts)
    {
        if (parts.Length != 4 || parts[0] != "team" || !long.TryParse(parts[1], out long poi) || !long.TryParse(parts[2], out long cargo))
            return false;
        var staff = MuseumData.Longs(parts[3]).Select(id => new StaffInstanceID(id)).ToList();
        if (staff.Count == 0) return false;
        ByPoi[poi] = new Loadout { Staff = staff, Cargo = cargo == 0 ? ExpeditionBonusModuleID.Null : new ExpeditionBonusModuleID(cargo) };
        return true;
    }
}
