using System;
using System.Collections.Generic;
using System.Linq;
using ExpeditionHelper.Logic.Game;
using TPS.Game;
using Unity.Entities;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Re-sends a finished expedition (same location, team, approach and cargo) until the enabled
/// goals are reached for every exhibit it can drop.
///
/// Lifecycle per location: the crate is waiting (stage Reward) → we snapshot the expedition, since
/// the game removes it once claimed → it disappears (claimed) → evaluate goals → queue a repeat →
/// wait until helipad, team, cargo and money allow it → start it like the setup screen does.
/// Notifications only on changes: first wait, start, final stop.
/// </summary>
internal sealed class RepeatExpeditions
{
    private const float PollIntervalSeconds = 1f;

    private sealed class Pending
    {
        public ExpeditionPlan Plan;
        public int Run;                 // runs completed so far in this chain
        public string LastWaitReason;   // wait reason with numbers stripped, to log changes of kind only
        public float WaitingSince = -1;
        public string StillNeeded = "";  // goals summary from when the run finished
        public InboxNotice Inbox;        // persistent "waiting" message, only for real waits
    }

    /// <summary>Waits shorter than this for the helicopter to land don't get an inbox message.</summary>
    private const float InboxAfterSeconds = 20f;

    private readonly Settings _settings;
    private float _nextPoll;

    // Every live expedition by POI id: its latest snapshot, chain run, and whether it got far
    // enough (Return/Reward) that disappearing means "claimed" rather than "cancelled".
    private readonly Dictionary<long, (ExpeditionPlan Plan, int Run, bool Returning)> _live = new();
    // Chain run counts for expeditions we started, by POI id.
    private readonly Dictionary<long, int> _chainRuns = new();
    private readonly List<Pending> _pending = new();
    // Last finished expedition per POI (kept even with repeating off), for manual repeats.
    private readonly Dictionary<long, ExpeditionPlan> _lastFinished = new();
    private ExpeditionGoals _lastGoals;

    public RepeatExpeditions(Settings settings) => _settings = settings;

    public void Update()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollIntervalSeconds;

        var manager = ExpeditionManager.Instance;
        if (manager == null) { Reset(); return; } // not in a museum

        // Snapshot every expedition each poll: auto-claim (or the player) can claim a crate within
        // the same second it lands, so we can't wait to see the Reward stage.
        var active = new HashSet<long>();
        foreach (var kv in manager._expeditions)
        {
            var e = kv.Value;
            long poi = ((WorldMapLocationPOIDefinitionID)e.POI).ID;
            active.Add(poi);
            bool returning = e.Stage is EExpeditionStage.Return or EExpeditionStage.Reward or EExpeditionStage.DropStaff;
            bool wasReturning = _live.TryGetValue(poi, out var known) && known.Returning;
            _live[poi] = (ExpeditionPlan.From(e), _chainRuns.GetValueOrDefault(poi), returning || wasReturning);
        }

        // Expeditions that are gone: claimed if they had come back, otherwise cancelled/failed.
        foreach (var poi in _live.Keys.Where(p => !active.Contains(p)).ToList())
        {
            var (plan, run, returning) = _live[poi];
            _live.Remove(poi);
            _chainRuns.Remove(poi);
            if (returning) OnFinished(plan, run + 1);
            else Log($"{plan.DisplayName}: ended before returning (cancelled or failed), not repeating");
        }

        foreach (var pending in _pending.ToList())
            TryStart(pending, active);
    }

    private void OnFinished(ExpeditionPlan plan, int runsDone)
    {
        _lastFinished[plan.Poi.ID] = plan;
        if (!_settings.RepeatExpeditions.Value || plan.Type != EExpeditionType.Expedition) return;

        string why = GoalsReached(plan);
        if (why == NoExhibits) { Log($"{plan.DisplayName}: no exhibits to collect here (materials/cash location), not repeating"); return; }
        if (why != null) { Stop(plan, why); return; }
        if (runsDone >= _settings.MaxRepeats.Value + 1)
        {
            int max = _settings.MaxRepeats.Value;
            Stop(plan, $"reached the limit of {max} repeat{(max == 1 ? "" : "s")}");
            return;
        }
        _pending.Add(new Pending { Plan = plan, Run = runsDone, StillNeeded = _lastGoals?.StillNeeded() ?? "" });
        Log($"{plan.DisplayName}: run {runsDone} done, repeat queued");
    }

    private const string NoExhibits = "no exhibits to collect at this location";

    /// <summary>null while there's still something to get; otherwise the reason to stop.</summary>
    private string GoalsReached(ExpeditionPlan plan)
    {
        bool wantPristine = _settings.UntilPristine.Value, wantKnowledge = _settings.UntilMaxKnowledge.Value;
        if (!wantPristine && !wantKnowledge) return null; // no goals: only MaxRepeats stops it

        var goals = ExpeditionGoals.Evaluate(plan, _settings.CountInventoryTowardsKnowledge.Value);
        _lastGoals = goals;
        if (goals.Exhibits.Count == 0) return NoExhibits;
        Log($"{plan.DisplayName}: {goals.Describe()}{(goals.PristinePossible ? "" : " — pristine can't drop at this survey level yet")}");
        bool done = (!wantPristine || goals.AllPristine) && (!wantKnowledge || goals.AllKnowledge);
        if (!done) return null;
        return wantPristine && wantKnowledge ? "pristine copies and max knowledge for every exhibit"
             : wantPristine ? "a pristine copy of every exhibit" : "max knowledge for every exhibit";
    }

    private void TryStart(Pending p, HashSet<long> activePois)
    {
        var plan = p.Plan;
        long poi = plan.Poi.ID;

        if (!_settings.RepeatExpeditions.Value) { Drop(p, null); return; }
        if (activePois.Contains(poi)) { Drop(p, null); return; } // the player sent one there already

        string hardStop = HardStopReason(plan);
        if (hardStop != null) { Drop(p, hardStop); return; }

        var settings = plan.BuildSettings(out string problem);
        string wait = WaitReason(plan) ?? problem;
        if (wait == null && settings != null)
            wait = MoneyCheck.WhyNotAffordable(settings.CalculateCost(), _settings);

        if (wait != null)
        {
            // Log when the kind of reason changes, not every time a number in it moves.
            string kind = new string(wait.Where(c => !char.IsDigit(c) && c != '.' && c != ',' && c != '-').ToArray());
            if (kind != p.LastWaitReason) Log($"{plan.DisplayName}: waiting — {wait}");
            p.LastWaitReason = kind;
            UpdateWaitingInbox(p, wait);
            return;
        }

        int cost = settings.CalculateCost();
        ExpeditionManager.Instance.AddExpedition(settings);
        _pending.Remove(p);
        p.Inbox?.Remove();
        _chainRuns[poi] = p.Run;
        Log($"{plan.DisplayName}: sent again (run {p.Run + 1}, cost {cost:N0})");
        Notify(NotifyKind.Started, $"Expedition repeated: {plan.DisplayName}", $"Sent again with the same team (run {p.Run + 1}, cost {cost:N0}).");
    }

    /// <summary>Conditions that won't fix themselves: give up instead of waiting forever.</summary>
    private static string HardStopReason(ExpeditionPlan plan)
    {
        foreach (var id in plan.Staff)
            if (!id.TryGet(out _)) return "a team member has left the museum or is missing";

        var cargo = plan.CargoModule;
        if (cargo != null && ExpeditionManager.Instance.ConsumableCargoItems.Contains(plan.Cargo)
            && !ExpeditionManager.Instance.HasCargoItem(plan.Cargo))
            return $"out of {cargo.name.Replace("CargoModule_", "")}";
        return null;
    }

    /// <summary>Temporary conditions: keep waiting.</summary>
    private static string WaitReason(ExpeditionPlan plan)
    {
        var manager = ExpeditionManager.Instance;
        if (manager.TryGetExpeditionForItem(plan.Helipad, out _)) return "the helipad is in use";
        if (!plan.Helipad.TryGet(out var helipad)) return "the helipad is gone";
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        if (em.HasComponent<ECExpeditionItem>(helipad.Entity))
        {
            var state = em.GetComponentData<ECExpeditionItem>(helipad.Entity).State;
            if (state != EExpeditionItemState.Idle) return $"the helicopter is still busy ({state})";
        }

        var poiDef = plan.PoiDefinition;
        foreach (var id in plan.Staff)
        {
            if (!id.TryGet(out var staff)) continue;
            string name = ExpeditionPlan.StaffName(staff);
            if (manager.TryGetExpeditionsForStaff(id, out var list) && list.Count > 0) return $"{name} is still on an expedition";
            if (!ExpeditionUtils.IsStaffValidForPOI(poiDef, staff, out var effect))
                return $"{name} can't go yet ({effect?.name?.Replace("StatusEffect_", "") ?? "status effect"})";
        }
        return null;
    }

    /// <summary>
    /// Keep one persistent inbox message per waiting repeat, with the current reason, once the wait
    /// is more than the helicopter landing (another reason, or longer than InboxAfterSeconds).
    /// </summary>
    private void UpdateWaitingInbox(Pending p, string wait)
    {
        float now = UnityEngine.Time.unscaledTime;
        if (p.WaitingSince < 0) p.WaitingSince = now;
        bool realWait = !wait.StartsWith("the helicopter") || now - p.WaitingSince > InboxAfterSeconds;
        if (!realWait || _settings.Notifications.Value != NotificationLevel.All) return;

        string text = $"{p.Plan.DisplayName} will be sent again with the same team (run {p.Run + 1}) as soon as it can go.\n\n" +
                      $"Waiting for: {wait}." +
                      (string.IsNullOrEmpty(p.StillNeeded) ? "" : $"\n\nStill to get here: {p.StillNeeded}.");
        if (p.Inbox == null || !p.Inbox.IsLive)
        {
            try
            {
                p.Inbox = InboxNotice.Send($"Repeat waiting: {p.Plan.DisplayName}", text, "Cancel repeat");
                if (p.Inbox != null) p.Inbox.Clicked += () => Drop(p, "cancelled");
            }
            catch (Exception e) { Entry.Host.Log.LogError($"[repeat] inbox message failed: {e}"); }
        }
        else p.Inbox.SetText(text);
    }

    private void Drop(Pending p, string reason)
    {
        _pending.Remove(p);
        p.Inbox?.Remove();
        if (reason != null) Stop(p.Plan, reason);
    }

    private void Stop(ExpeditionPlan plan, string reason)
    {
        Log($"{plan.DisplayName}: not repeating — {reason}");
        Notify(NotifyKind.Stopped, $"Repeat stopped: {plan.DisplayName}", $"Not repeating: {reason}.");
    }

    private enum NotifyKind { Started, Stopped }

    private AdvisorNotice Notify(NotifyKind kind, string title, string text, string action = null)
    {
        var level = _settings.Notifications.Value;
        if (level == NotificationLevel.None) return null;
        if (level == NotificationLevel.StopsOnly && kind != NotifyKind.Stopped) return null;
        try { return AdvisorNotice.Show(title, text, action, displayTime: 10f); }
        catch (Exception e) { Entry.Host.Log.LogError($"[repeat] notification failed: {e}"); return null; }
    }

    private void Reset()
    {
        foreach (var p in _pending) p.Inbox?.Remove();
        _pending.Clear();
        _live.Clear();
        _chainRuns.Clear();
    }

    /// <summary>
    /// Start a repeat of the last finished expedition whose location name contains `nameFilter`,
    /// now, after the same checks as the automatic repeat (goals excepted). Returns what happened.
    /// </summary>
    public string StartNow(string nameFilter)
    {
        var plan = _lastFinished.Values.FirstOrDefault(p => p.PoiName.IndexOf(nameFilter ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
        if (plan == null)
            return $"no finished expedition matching '{nameFilter}'. Known: {string.Join(", ", _lastFinished.Values.Select(p => p.DisplayName))}";

        var manager = ExpeditionManager.Instance;
        if (manager == null) return "not in a museum";
        foreach (var kv in manager._expeditions)
            if (((WorldMapLocationPOIDefinitionID)kv.Value.POI).ID == plan.Poi.ID) return $"{plan.DisplayName} already has an expedition";

        string blocked = HardStopReason(plan) ?? WaitReason(plan);
        var settings = blocked == null ? plan.BuildSettings(out blocked) : null;
        if (blocked != null) return $"not started: {blocked}";
        int cost = settings.CalculateCost();
        string money = MoneyCheck.WhyNotAffordable(cost, _settings);
        if (money != null) return $"not started: {money}";

        long before = FinanceManager.Instance.Balance;
        manager.AddExpedition(settings);
        Log($"{plan.DisplayName}: sent again manually (cost {cost:N0})");
        return $"started {plan.DisplayName}: cost {cost:N0}, cash {before:N0} -> {FinanceManager.Instance.Balance:N0}";
    }

    /// <summary>Dev aid: what a repeat of each current expedition would decide right now, without starting anything.</summary>
    public string DryRun()
    {
        var manager = ExpeditionManager.Instance;
        if (manager == null) return "not in a museum";
        var lines = new List<string>();
        foreach (var kv in manager._expeditions)
        {
            var plan = ExpeditionPlan.From(kv.Value);
            var goals = ExpeditionGoals.Evaluate(plan, _settings.CountInventoryTowardsKnowledge.Value);
        _lastGoals = goals;
            var settings = plan.BuildSettings(out string problem);
            int cost = settings?.CalculateCost() ?? -1;
            lines.Add($"{plan.DisplayName} (stage {kv.Value.Stage}, cargo {plan.CargoModule?.name ?? "none"}, alternate={goals.UsesAlternateRewards}, pristinePossible={goals.PristinePossible})");
            lines.Add($"  goals: {goals.Describe()}");
            lines.Add($"  goals reached: {GoalsReached(plan) ?? "no"}");
            lines.Add($"  hard stop: {HardStopReason(plan) ?? "none"}; wait: {WaitReason(plan) ?? "none"}; build settings: {problem ?? "ok"}");
            lines.Add($"  cost {cost:N0}; money: {(cost >= 0 ? MoneyCheck.WhyNotAffordable(cost, _settings) ?? "ok" : "n/a")}; bills {MoneyCheck.MonthEndBills():N0}; projected profit {MoneyCheck.ProjectedProfit():N0}");
        }
        return lines.Count == 0 ? "no expeditions" : string.Join("\n", lines);
    }

    private static void Log(string message) => Entry.Host.Log.LogInfo($"[repeat] {message}");
}
