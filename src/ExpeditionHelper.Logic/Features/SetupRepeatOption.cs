using System;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// "Repeat until done" on the expedition setup screen: the per-location repeat choice, with a
/// status line saying what's still to get there (the same goal check the repeats use).
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

    public SetupRepeatOption(Settings settings)
    {
        _settings = settings;
        if (!ExpeditionSetupSection.Install("Repeat", "Repeat until done")) return;
        ExpeditionSetupSection.Toggled += OnToggled;
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
        string key = $"{poi}|{settings.Approach}|{settings.CargoModule.ID}|{settings.ExpeditionType}|{on}";
        if (key == _lastKey && now < _nextStatus) return;
        _lastKey = key;
        _nextStatus = now + StatusRefreshSeconds;

        bool visible = settings.ExpeditionType == EExpeditionType.Expedition;
        if (!visible) { ExpeditionSetupSection.Set(false, false, true, ""); return; }

        ExpeditionGoals goals = null;
        try { goals = ExpeditionGoals.Evaluate(ExpeditionPlan.From(settings), _settings.CountInventoryTowardsKnowledge.Value); }
        catch (Exception e) { Entry.Host.Log.LogError($"[setup] goal check failed: {e}"); }

        // Repeats only collect exhibits: nothing to repeat for at materials-only locations.
        if (goals != null && goals.Exhibits.Count == 0)
        {
            ExpeditionSetupSection.Set(true, false, false, "No exhibits to find here, so there's nothing to repeat for.");
            return;
        }
        ExpeditionSetupSection.Set(true, on, true, Status(goals, on));
    }

    private void OnToggled(bool on)
    {
        var settings = ExpeditionSetupSection.OpenMenu?._settings;
        if (settings == null) return;
        long poi = ((WorldMapLocationPOIDefinitionID)settings.Definition).ID;
        RepeatChoices.Set(poi, on);
        Entry.Host.Log.LogInfo($"[repeat] {ExpeditionPlan.From(settings).DisplayName}: repeat {(on ? "on" : "off")} (setup screen)");
        _lastKey = null;
        _nextPoll = 0;
        Update();
    }

    private string Status(ExpeditionGoals goals, bool on)
    {
        if (!on) return "Runs once.";
        int max = _settings.MaxRepeats.Value;
        string limit = $"at most {max} repeat{(max == 1 ? "" : "s")}";
        bool wantPristine = _settings.UntilPristine.Value, wantKnowledge = _settings.UntilMaxKnowledge.Value;
        if (!wantPristine && !wantKnowledge) return $"Repeats {limit} (no goals set).";
        if (goals == null) return $"Repeats until done, {limit}.";

        bool done = (!wantPristine || goals.AllPristine) && (!wantKnowledge || goals.AllKnowledge);
        if (done) return "You already have everything from here, so it runs once.";

        string note = wantPristine && !goals.AllPristine && !goals.PristinePossible
            ? "\n(Pristine can't drop at this survey level yet.)" : "";
        return $"Repeats until done, {limit}. Still to get:\n{goals.MissingByGoal(wantPristine, wantKnowledge)}{note}";
    }
}
