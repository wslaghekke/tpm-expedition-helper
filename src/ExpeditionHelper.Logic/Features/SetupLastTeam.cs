using System;
using System.Collections.Generic;
using System.Linq;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// "Last team" on the expedition setup screen: puts the team (and cargo) last sent to this
/// location back into the staff slots, whichever helipad the screen was opened from. Team members
/// who can't go right now (away, recovering, gone, no longer fitting a slot) leave their slot empty.
/// </summary>
internal sealed class SetupLastTeam
{
    private const float PollIntervalSeconds = 0.5f;
    private float _nextPoll;

    public SetupLastTeam()
    {
        ExpeditionSetupSection.LastTeamClicked += OnClicked;
    }

    public void Update()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollIntervalSeconds;

        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        ExpeditionSetupSection.SetLastTeam(LastLoadouts.Get(PoiOf(settings)) != null);
    }

    private static long PoiOf(ExpeditionSettings settings) => ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;

    private static void OnClicked()
    {
        var menu = ExpeditionSetupSection.OpenMenu;
        var settings = menu?._settings;
        if (settings == null) return;
        var last = LastLoadouts.Get(PoiOf(settings));
        if (last == null) return;
        string name = ExpeditionPlan.From(settings).DisplayName;

        try
        {
            var missing = new List<string>();
            var available = new List<StaffInstance>();
            foreach (var id in last.Staff)
            {
                if (!id.TryGet(out var staff)) { missing.Add("a team member who left"); continue; }
                string why = WhyUnavailable(settings, staff);
                if (why == null) available.Add(staff);
                else missing.Add($"{ExpeditionPlan.StaffName(staff)} ({why})");
            }

            // Same slot fitting as a repeat (ExpeditionPlan.BuildSettings), but empty slots are fine here.
            var filled = new List<string>();
            foreach (var slot in settings.AssignedStaff)
            {
                var match = available.FirstOrDefault(s => slot.RequiredStaff.IsValidForExpedition(s));
                slot.Staff = match != null ? match : StaffInstanceID.Null;
                if (match == null) continue;
                available.Remove(match);
                filled.Add(ExpeditionPlan.StaffName(match));
            }
            foreach (var s in available) missing.Add($"{ExpeditionPlan.StaffName(s)} (no matching slot)");

            string cargo = SetCargo(settings, last);
            menu.RefreshData();
            Entry.Host.Log.LogInfo($"[setup] {name}: last team — {(filled.Count == 0 ? "nobody" : string.Join(", ", filled))}" +
                                   (missing.Count == 0 ? "" : $"; can't go: {string.Join(", ", missing)}") + cargo);
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[setup] {name}: filling in the last team failed: {e}");
        }
    }

    /// <summary>Null if the staff member can go on this expedition now, otherwise why not.</summary>
    private static string WhyUnavailable(ExpeditionSettings settings, StaffInstance staff)
    {
        var manager = ExpeditionManager.Instance;
        if (manager.TryGetExpeditionsForStaff(staff, out var list) && list.Count > 0) return "on an expedition";
        var poi = LevelDatabaseUtils.Get((WorldMapLocationPOIDefinitionID)settings.Definition);
        if (!ExpeditionUtils.IsStaffValidForPOI(poi, staff, out var effect))
            return effect?.name?.Replace("StatusEffect_", "") ?? "status effect";
        return null;
    }

    /// <summary>The last cargo if there's still one to take (else the current choice stays); returns a log note.</summary>
    private static string SetCargo(ExpeditionSettings settings, LastLoadouts.Loadout last)
    {
        if (last.Cargo == settings.CargoModule) return "";
        if (last.Cargo == ExpeditionBonusModuleID.Null)
        {
            settings.CargoModule = ExpeditionBonusModuleID.Null;
            return "; no cargo";
        }
        var cargo = last.CargoModule;
        foreach (var option in settings.AvailableCargo.Iter())
            if (option != null && option.Pointer == cargo?.Pointer)
            {
                settings.CargoModule = last.Cargo;
                return $"; cargo {cargo.name.Replace("CargoModule_", "")}";
            }
        return $"; cargo {cargo?.name.Replace("CargoModule_", "") ?? "?"} not available, kept the current one";
    }
}
