using System;
using System.Collections.Generic;
using System.Linq;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// "Repeat until done" on the expedition setup screen: the per-location repeat choice, a goal to
/// repeat for instead of the exhibit goals (a perk or a number of it, or a stock target for a material, where the
/// location drops those), and a status line saying what's still to get there (the same goal check
/// the repeats use).
///
/// The screen's settings change through many paths (team, cargo, approach, switching location),
/// so they're polled while the screen is open rather than hooked: every frame a cheap key of what
/// the answer depends on (polling slower showed the previous location's section for a moment after
/// switching), and the full check only when it changed. The game is paused on the expedition map,
/// so stock and knowledge don't change behind it; should time run anyway, it's also redone every
/// few seconds.
/// </summary>
internal sealed class SetupRepeatOption
{
    private const float StatusRefreshSeconds = 3f;

    private readonly Settings _settings;
    private float _nextStatus;
    // Everything the section depends on that the screen or the player can change, and how many of
    // the picked perk or material the player has; a tuple, not a string, as it's built every frame.
    private (long Poi, EExpeditionApproach Approach, long Cargo, EExpeditionType Type, bool On,
        (long, int, int) Perk, (long, int) Stock, int StockBatch, int Have)? _lastKey;
    private IntPtr _preloadedFor;
    private List<PerkRewards.Option> _perks = new();
    private List<MaterialRewards.Option> _materials = new();
    private List<Goal> _goals = new(); // as shown in the dropdown, after its first entry
    private Goal _single; // the location's only goal, without exhibits: a toggle instead of the dropdown

    /// <summary>One dropdown entry: a perk or a material (how many is picked in the quantity row).</summary>
    private sealed class Goal
    {
        public string Text;
        public PerkRewards.Option Perk;
        public MaterialRewards.Option Material;
    }

    public SetupRepeatOption(Settings settings)
    {
        _settings = settings;
        if (!ExpeditionSetupSection.Install("Repeat", ToggleLabel)) return;
        ExpeditionSetupSection.Toggled += OnToggled;
        ExpeditionSetupSection.GoalChosen += OnGoalChosen;
        ExpeditionSetupSection.QuantityChosen += OnQuantityChosen;
    }

    public void Update()
    {
        float now = UnityEngine.Time.unscaledTime;

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

        // Recompute when anything that affects the answer changed, and every few seconds while
        // game time runs (inventory, knowledge and stock could change then).
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        bool on = RepeatChoices.IsOn(_settings, poi);
        var perkGoal = RepeatChoices.PerkGoal(poi);
        long perk = perkGoal.Perk;
        var stock = RepeatChoices.Stock(poi);
        int have = perk != 0 ? PerkRewards.Owned(perk) : stock.Material != 0 ? MaterialRewards.Stock(stock.Material) : 0;
        var key = (poi, settings.Approach, settings.CargoModule.ID, settings.ExpeditionType, on, perkGoal, stock, RepeatChoices.StockBatch(poi), have);
        bool timeRuns = UnityEngine.Time.timeScale > 0;
        if (key == _lastKey && (!timeRuns || now < _nextStatus)) return;
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

        // Without exhibits the toggle alone would do nothing: the dropdown decides (a pick repeats,
        // "Choose ..." runs once), so keep the stored choice in line with it.
        var pickedPerk = _perks.FirstOrDefault(p => p.Id == perk);
        var pickedMaterial = _materials.FirstOrDefault(m => m.Id == stock.Material);
        if (!hasExhibits)
        {
            bool hasGoal = perk != 0 || stock.Material != 0;
            if (on != hasGoal) { RepeatChoices.Set(_settings, poi, hasGoal); on = hasGoal; }
        }

        _goals = BuildGoals();
        // Only one thing to repeat for and no exhibits: nothing to choose, so the toggle switches that goal on and off.
        _single = !hasExhibits && _goals.Count == 1 ? _goals[0] : null;
        if (_single != null)
        {
            ExpeditionSetupSection.SetGoals(null, null, 0, false);
            ExpeditionSetupSection.SetToggleLabel(Title());
        }
        else
        {
            var options = new List<string> { hasExhibits ? "None (exhibit goals)" : $"Choose {What()}" };
            options.AddRange(_goals.Select(g => g.Text));
            int chosen = _goals.FindIndex(g => perk != 0 ? g.Perk?.Id == perk : g.Material != null && g.Material.Id == stock.Material);
            ExpeditionSetupSection.SetGoals(Title(), _goals.Count > 0 ? options.ToArray() : null, chosen + 1, true);
            ExpeditionSetupSection.SetToggleLabel(ToggleLabel);
        }

        // How many: xN of a perk the game counts and the player has found here, or N steps of a material.
        if (pickedPerk != null && Countable(pickedPerk))
            ExpeditionSetupSection.SetQuantity(true, QuantityIndex(perkGoal.Amount > 0 ? perkGoal.Batch : 0));
        else if (perk == 0 && pickedMaterial != null)
            ExpeditionSetupSection.SetQuantity(true, QuantityIndex(RepeatChoices.StockBatch(poi)),
                ExpeditionSetupSection.Quantities.Select(n => $"+{n * pickedMaterial.Step:N0}").ToArray());
        else
            ExpeditionSetupSection.SetQuantity(false, -1);
        ExpeditionSetupSection.Set(true, on, true, Status(settings, goals, on, perkGoal, stock, hasExhibits), showToggle: hasExhibits || _single != null);
    }

    private const string ToggleLabel = "Repeat until done";

    /// <summary>The quantity button for a batch (x1 for picks from before the buttons, batch 0).</summary>
    private static int QuantityIndex(int batch) => Math.Max(0, Array.IndexOf(ExpeditionSetupSection.Quantities, batch));

    /// <summary>Perks first, then materials; how many is picked in the quantity row below the dropdown.</summary>
    private List<Goal> BuildGoals()
    {
        var list = _perks.Select(p => new Goal { Perk = p, Text = p.Available ? Label(p) : $"{Label(p)} (survey level {p.UnlockLevel})" }).ToList();
        list.AddRange(_materials.Select(m => new Goal { Material = m, Text = m.Name }));
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
        if (_single != null) Pick(settings, poi, on ? _single : null); // the toggle stands in for the dropdown
        else
        {
            RepeatChoices.Set(_settings, poi, on);
            Entry.Host.Log.LogInfo($"[repeat] {ExpeditionPlan.From(settings).DisplayName}: repeat {(on ? "on" : "off")} (setup screen)");
        }
        Refresh();
    }

    /// <summary>Index 0 is "no goal"; the rest follow <see cref="_goals"/>. Picking a goal also turns repeating on.</summary>
    private void OnGoalChosen(int index)
    {
        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        Pick(settings, poi, index >= 1 && index <= _goals.Count ? _goals[index - 1] : null);
        Refresh();
    }

    /// <summary>Repeat for a goal at this location (x1 of it until another amount is picked), or for none.</summary>
    private void Pick(ExpeditionSettings settings, long poi, Goal goal)
    {
        var perk = goal?.Perk;
        var material = goal?.Material;
        if (perk != null && Countable(perk)) RepeatChoices.SetPerk(poi, perk.Id, perk.Owned + 1, 1);
        else RepeatChoices.SetPerk(poi, perk?.Id ?? 0);
        if (material != null) RepeatChoices.SetStock(poi, material.Id, material.Stock + material.Step, 1);
        else RepeatChoices.SetStock(poi, 0, 0);
        if (goal != null) RepeatChoices.Set(_settings, poi, true);
        string what = goal == null ? "none"
            : perk != null ? $"perk {perk.Name}{(perk.Discovered ? "" : " (undiscovered)")}"
            : $"{material.Stock + material.Step:N0} {material.Name} in stock";
        Entry.Host.Log.LogInfo($"[repeat] {ExpeditionPlan.From(settings).DisplayName}: repeat for {what} (setup screen)");
    }

    /// <summary>
    /// "xN" picked for the chosen perk or material: repeat until the player has N more of the perk,
    /// or N steps (about a trip's worth each) more of the material, than now.
    /// </summary>
    private void OnQuantityChosen(int count)
    {
        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        string place = ExpeditionPlan.From(settings).DisplayName;
        var perk = _perks.FirstOrDefault(p => p.Id == RepeatChoices.Perk(poi));
        var material = _materials.FirstOrDefault(m => m.Id == RepeatChoices.Stock(poi).Material);
        if (perk != null && Countable(perk))
        {
            int owned = PerkRewards.Owned(perk.Id);
            RepeatChoices.SetPerk(poi, perk.Id, owned + count, count);
            Entry.Host.Log.LogInfo($"[repeat] {place}: repeat until {owned + count} {perk.Name} (x{count}, have {owned}) (setup screen)");
        }
        else if (perk == null && material != null)
        {
            int have = MaterialRewards.Stock(material.Id);
            int target = have + count * material.Step;
            RepeatChoices.SetStock(poi, material.Id, target, count);
            Entry.Host.Log.LogInfo($"[repeat] {place}: repeat until {target:N0} {material.Name} (+{count * material.Step:N0}, have {have:N0}) (setup screen)");
        }
        else return;
        RepeatChoices.Set(_settings, poi, true);
        Refresh();
    }

    /// <summary>The repeat limit for amount goals, which counts only runs that brought none of it.</summary>
    private static string DryLimit(int max, string what) =>
        $"stopping after {max} run{(max == 1 ? "" : "s")} in a row without {what}";

    /// <summary>Perks the player can pick an amount for: found here before, and counted by the game.</summary>
    private static bool Countable(PerkRewards.Option perk) => perk.Discovered && perk.Owned >= 0;

    private void Refresh()
    {
        _lastKey = null;
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

    private string Status(ExpeditionSettings settings, ExpeditionGoals goals, bool on, (long Perk, int Amount, int Batch) perkGoal, (long Material, int Amount) stock, bool hasExhibits)
    {
        if (!on) return hasExhibits || _single != null ? "Runs once." : $"Runs once. Choose {What()} to repeat for.";
        int max = _settings.MaxRepeats.Value;
        string limit = $"at most {max} repeat{(max == 1 ? "" : "s")}";

        long perk = perkGoal.Perk;
        if (perk != 0)
        {
            var option = _perks.FirstOrDefault(o => o.Id == perk);
            if (option == null) return $"{PerkRewards.Shown(settings.Definition, perk)} can't drop here with this cargo, so it runs once.";
            string name = option.Discovered ? option.Name : Label(option).ToLowerInvariant();
            string note = option.Available ? "" : $"\n({name} only drops from survey level {option.UnlockLevel}.)";
            if (perkGoal.Amount > 0 && option.Owned >= 0)
            {
                if (option.Owned >= perkGoal.Amount) return $"You already have {option.Owned:N0} {name}, so it runs once.";
                return $"Repeats until you have {perkGoal.Amount:N0} {name} (now {option.Owned:N0}), {DryLimit(max, $"a {name}")}.{note}";
            }
            return $"Repeats until {name} drops, {limit}.{note}";
        }
        if (stock.Material != 0)
        {
            var material = _materials.FirstOrDefault(m => m.Id == stock.Material);
            if (material == null) return $"{MaterialRewards.Name(stock.Material)} can't drop here with this cargo, so it runs once.";
            if (material.Stock >= stock.Amount) return $"You already have {material.Stock:N0} {material.Name}, so it runs once.";
            return $"Repeats until you have {stock.Amount:N0} {material.Name} (now {material.Stock:N0}), {DryLimit(max, $"more {material.Name}")}.";
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
