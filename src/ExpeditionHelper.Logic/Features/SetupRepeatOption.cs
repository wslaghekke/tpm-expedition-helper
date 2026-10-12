using System;
using System.Collections.Generic;
using System.Linq;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// "Repeat until done" on the expedition setup screen: the per-location repeat choice, a goal to
/// repeat for instead of the exhibit goals (a perk, or a stock target for a material, where the
/// location drops those), and a status line saying what's still to get there (the same goal check
/// the repeats use).
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
    private List<PerkRewards.Option> _perks = new();
    private List<MaterialRewards.Option> _materials = new();
    private List<Goal> _goals = new(); // as shown in the dropdown, after its first entry

    /// <summary>One dropdown entry: a perk, or an amount of a material.</summary>
    private sealed class Goal
    {
        public string Text;
        public PerkRewards.Option Perk;
        public MaterialRewards.Option Material;
        public int Amount;
    }

    public SetupRepeatOption(Settings settings)
    {
        _settings = settings;
        if (!ExpeditionSetupSection.Install("Repeat", "Repeat until done")) return;
        ExpeditionSetupSection.Toggled += OnToggled;
        ExpeditionSetupSection.GoalChosen += OnGoalChosen;
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
        // (inventory, knowledge and stock change while the screen is open).
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        bool on = RepeatChoices.IsOn(_settings, poi);
        long perk = RepeatChoices.Perk(poi);
        var stock = RepeatChoices.Stock(poi);
        string key = $"{poi}|{settings.Approach}|{settings.CargoModule.ID}|{settings.ExpeditionType}|{on}|{perk}|{stock}";
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
        try { _materials = MaterialRewards.For(plan); }
        catch (Exception e) { Entry.Host.Log.LogError($"[setup] material check failed: {e}"); _materials = new List<MaterialRewards.Option>(); }

        // Repeats collect exhibits, a chosen perk or a stock target: nothing to repeat for elsewhere (cash locations).
        bool hasExhibits = goals == null || goals.Exhibits.Count > 0;
        if (!hasExhibits && _perks.Count == 0 && _materials.Count == 0)
        {
            ExpeditionSetupSection.SetGoals(null, null, 0, false);
            ExpeditionSetupSection.Set(true, false, false, "No exhibits, perks or materials to find here, so there's nothing to repeat for.");
            return;
        }

        _goals = BuildGoals(stock);
        var options = new List<string> { hasExhibits ? "None (exhibit goals)" : $"Choose {What()}" };
        options.AddRange(_goals.Select(g => g.Text));
        int chosen = _goals.FindIndex(g => perk != 0 ? g.Perk?.Id == perk : g.Material != null && g.Material.Id == stock.Material && g.Amount == stock.Amount);
        ExpeditionSetupSection.SetGoals(Title(), _goals.Count > 0 ? options.ToArray() : null, chosen + 1, true);
        ExpeditionSetupSection.Set(true, on, true, Status(settings, goals, on, perk, stock, hasExhibits));
    }

    /// <summary>Perks first, then for each material a few stock targets above what you have (and the chosen one).</summary>
    private List<Goal> BuildGoals((long Material, int Amount) stock)
    {
        var list = _perks.Select(p => new Goal { Perk = p, Text = p.Available ? Label(p) : $"{Label(p)} (survey level {p.UnlockLevel})" }).ToList();
        foreach (var m in _materials)
        {
            var targets = MaterialRewards.Targets(m);
            if (stock.Material == m.Id && !targets.Contains(stock.Amount)) { targets.Add(stock.Amount); targets.Sort(); }
            list.AddRange(targets.Select(t => new Goal
            {
                Material = m, Amount = t,
                Text = t > m.Stock ? $"{t:N0} {m.Name} (+{t - m.Stock:N0})" : $"{t:N0} {m.Name} (have {m.Stock:N0})",
            }));
        }
        return list;
    }

    private string Title() =>
        _materials.Count == 0 ? "Repeat for perk" : _perks.Count == 0 ? "Repeat until in stock" : "Repeat for";

    private string What() => _materials.Count == 0 ? "a perk" : _perks.Count == 0 ? "an amount" : "a goal";

    private void OnToggled(bool on)
    {
        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        RepeatChoices.Set(_settings, poi, on);
        Entry.Host.Log.LogInfo($"[repeat] {ExpeditionPlan.From(settings).DisplayName}: repeat {(on ? "on" : "off")} (setup screen)");
        Refresh();
    }

    /// <summary>Index 0 is "no goal"; the rest follow <see cref="_goals"/>. Picking a goal also turns repeating on.</summary>
    private void OnGoalChosen(int index)
    {
        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        var goal = index >= 1 && index <= _goals.Count ? _goals[index - 1] : null;
        RepeatChoices.SetPerk(poi, goal?.Perk?.Id ?? 0);
        RepeatChoices.SetStock(poi, goal?.Material?.Id ?? 0, goal?.Amount ?? 0);
        if (goal != null) RepeatChoices.Set(_settings, poi, true);
        string what = goal == null ? "none"
            : goal.Perk != null ? $"perk {goal.Perk.Name}{(goal.Perk.Discovered ? "" : " (undiscovered)")}"
            : $"{goal.Amount} {goal.Material.Name} in stock";
        Entry.Host.Log.LogInfo($"[repeat] {ExpeditionPlan.From(settings).DisplayName}: repeat for {what} (setup screen)");
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

    private string Status(ExpeditionSettings settings, ExpeditionGoals goals, bool on, long perk, (long Material, int Amount) stock, bool hasExhibits)
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
        if (stock.Material != 0)
        {
            var material = _materials.FirstOrDefault(m => m.Id == stock.Material);
            if (material == null) return $"{MaterialRewards.Name(stock.Material)} can't drop here with this cargo, so it runs once.";
            if (material.Stock >= stock.Amount) return $"You already have {material.Stock:N0} {material.Name}, so it runs once.";
            return $"Repeats until you have {stock.Amount:N0} {material.Name} (now {material.Stock:N0}), {limit}.";
        }
        if (!hasExhibits) return $"Runs once. Choose {What()} to repeat for.";

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
