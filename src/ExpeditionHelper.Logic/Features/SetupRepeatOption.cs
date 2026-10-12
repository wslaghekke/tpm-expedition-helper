using System;
using System.Collections.Generic;
using System.Linq;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// "Repeat until done" on the expedition setup screen: the per-location repeat choice, a perk to
/// repeat for (at locations that drop perks), and a status line saying what's still to get there
/// (the same goal check the repeats use).
///
/// The screen's settings change through many paths (team, cargo, approach, switching location),
/// so the status is refreshed by polling while the screen is open rather than via more hooks.
/// </summary>
internal sealed class SetupRepeatOption
{
    private const float PollIntervalSeconds = 0.5f;
    private const float StatusRefreshSeconds = 3f;

    private readonly Settings _settings;
    private float _nextPoll, _nextStatus;
    private string _lastKey;
    private IntPtr _preloadedFor;
    private List<PerkRewards.Option> _perks = new(); // as shown in the dropdown, after its first entry

    public SetupRepeatOption(Settings settings)
    {
        _settings = settings;
        if (!ExpeditionSetupSection.Install("Repeat", "Repeat until done")) return;
        ExpeditionSetupSection.Toggled += OnToggled;
        ExpeditionSetupSection.PerkChosen += OnPerkChosen;
    }

    public void Update()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollIntervalSeconds;

        // Once per loaded museum: start loading the toggle template before the setup screen needs it.
        var manager = ExpeditionManager.Instance;
        if (manager != null && manager.Pointer != _preloadedFor)
        {
            _preloadedFor = manager.Pointer;
            UiTemplates.Preload();
        }

        var menu = ExpeditionSetupSection.OpenMenu;
        var settings = menu?._settings;
        if (settings == null) { _lastKey = null; return; }

        // Recompute when anything that affects the answer changed, and every few seconds anyway
        // (inventory and knowledge change while the screen is open).
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        bool on = RepeatChoices.IsOn(_settings, poi);
        long perk = RepeatChoices.Perk(poi);
        string key = $"{poi}|{settings.Approach}|{settings.CargoModule.ID}|{settings.ExpeditionType}|{on}|{perk}";
        if (key == _lastKey && now < _nextStatus) return;
        _lastKey = key;
        _nextStatus = now + StatusRefreshSeconds;

        bool visible = settings.ExpeditionType == EExpeditionType.Expedition;
        if (!visible) { ExpeditionSetupSection.Set(false, false, true, ""); return; }

        var plan = ExpeditionPlan.From(settings);
        ExpeditionGoals goals = null;
        try { goals = ExpeditionGoals.Evaluate(plan, _settings.CountInventoryTowardsKnowledge.Value); }
        catch (Exception e) { Entry.Host.Log.LogError($"[setup] goal check failed: {e}"); }
        try { _perks = PerkRewards.For(plan); }
        catch (Exception e) { Entry.Host.Log.LogError($"[setup] perk check failed: {e}"); _perks = new List<PerkRewards.Option>(); }

        // Repeats only collect exhibits or a chosen perk: nothing to repeat for at materials-only locations.
        bool hasExhibits = goals == null || goals.Exhibits.Count > 0;
        if (!hasExhibits && _perks.Count == 0)
        {
            ExpeditionSetupSection.SetPerks(null, 0, false);
            ExpeditionSetupSection.Set(true, false, false, "No exhibits or perks to find here, so there's nothing to repeat for.");
            return;
        }

        var options = new List<string> { hasExhibits ? "None (exhibit goals)" : "Choose a perk" };
        options.AddRange(_perks.Select(o => o.Available ? Label(o) : $"{Label(o)} (survey level {o.UnlockLevel})"));
        int chosen = _perks.FindIndex(o => o.Id == perk);
        ExpeditionSetupSection.SetPerks(_perks.Count > 0 ? options.ToArray() : null, chosen + 1, true);
        ExpeditionSetupSection.Set(true, on, true, Status(settings, goals, on, perk, hasExhibits));
    }

    private void OnToggled(bool on)
    {
        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        RepeatChoices.Set(_settings, poi, on);
        Entry.Host.Log.LogInfo($"[repeat] {ExpeditionPlan.From(settings).DisplayName}: repeat {(on ? "on" : "off")} (setup screen)");
        Refresh();
    }

    /// <summary>Index 0 is "no perk"; the rest follow <see cref="_perks"/>. Picking a perk also turns repeating on.</summary>
    private void OnPerkChosen(int index)
    {
        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        var option = index >= 1 && index <= _perks.Count ? _perks[index - 1] : null;
        RepeatChoices.SetPerk(poi, option?.Id ?? 0);
        if (option != null) RepeatChoices.Set(_settings, poi, true);
        Entry.Host.Log.LogInfo($"[repeat] {ExpeditionPlan.From(settings).DisplayName}: repeat for perk {(option == null ? "none" : $"{option.Name}{(option.Discovered ? "" : " (undiscovered)")}")} (setup screen)");
        Refresh();
    }

    private void Refresh()
    {
        _lastKey = null;
        _nextPoll = 0;
        Update();
    }

    /// <summary>
    /// The perk's name, or "Undiscovered perk" (numbered when there are several) for perks not
    /// found here yet: like exhibits, they're not named until discovered.
    /// </summary>
    private string Label(PerkRewards.Option option)
    {
        if (option.Discovered) return option.Name;
        var undiscovered = _perks.Where(o => !o.Discovered).ToList();
        return undiscovered.Count == 1 ? "Undiscovered perk" : $"Undiscovered perk {undiscovered.IndexOf(option) + 1}";
    }

    private string Status(ExpeditionSettings settings, ExpeditionGoals goals, bool on, long perk, bool hasExhibits)
    {
        if (!on) return "Runs once.";
        int max = _settings.MaxRepeats.Value;
        string limit = $"at most {max} repeat{(max == 1 ? "" : "s")}";

        if (perk != 0)
        {
            var option = _perks.FirstOrDefault(o => o.Id == perk);
            if (option == null) return $"{PerkRewards.Shown(settings.Definition, perk)} can't drop here with this cargo, so it runs once.";
            string name = option.Discovered ? option.Name : Label(option).ToLowerInvariant();
            string note = option.Available ? "" : $"\n({name} only drops from survey level {option.UnlockLevel}.)";
            return $"Repeats until {name} drops, {limit}.{note}";
        }
        if (!hasExhibits) return "Runs once. Choose a perk to repeat for.";

        bool wantPristine = _settings.UntilPristine.Value, wantKnowledge = _settings.UntilMaxKnowledge.Value;
        if (!wantPristine && !wantKnowledge) return $"Repeats {limit} (no goals set).";
        if (goals == null) return $"Repeats until done, {limit}.";

        bool done = (!wantPristine || goals.AllPristine) && (!wantKnowledge || goals.AllKnowledge);
        if (done) return "You already have everything from here, so it runs once.";

        string pristineNote = wantPristine && !goals.AllPristine && !goals.PristinePossible
            ? "\n(Pristine can't drop at this survey level yet.)" : "";
        return $"Repeats until done, {limit}. Still to get:\n{goals.MissingByGoal(wantPristine, wantKnowledge)}{pristineNote}";
    }
}
