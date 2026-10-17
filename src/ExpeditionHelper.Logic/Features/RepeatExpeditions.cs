using System;
using System.Collections.Generic;
using System.Linq;
using ExpeditionHelper.Logic.Game;
using TPS.Game;
using Unity.Entities;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Re-sends a finished expedition (same location, team, approach and cargo) until the enabled
/// goals are reached for every exhibit it can drop, or, if a perk was picked for the location on
/// the setup screen, until a crate brings that perk back (or the player has the chosen number of
/// it), or, with a stock target picked, until the player has that much of the material. Art studio
/// sessions repeat a picked number of times instead, each once the player has revealed the last
/// piece (and, by default, the artists are fully rested).
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
        public int Dry;                 // of those, the latest runs in a row that brought nothing towards an amount goal
        public string LastWaitReason;   // wait reason with numbers stripped, to log changes of kind only
        public float WaitingSince = -1;
        public string StillNeeded = "";  // goals summary from when the run finished
        public InboxNotice Inbox;        // persistent "waiting" message, only for real waits
    }

    /// <summary>Waits shorter than this for the helicopter to land don't get an inbox message.</summary>
    private const float InboxAfterSeconds = 20f;

    private readonly Settings _settings;
    private float _nextPoll;

    // Every live expedition by POI id: its latest snapshot, chain run, whether it got far enough
    // (Return/Reward) that disappearing means "claimed" rather than "cancelled", and the perks
    // seen in its crate; and for a stock goal, the stock when it was first seen (to tell whether
    // the run brought any).
    private sealed class Live
    {
        public ExpeditionPlan Plan;
        public int Run, Dry;
        public bool Returning;
        public readonly HashSet<long> Perks = new();
        public int StockBefore = -1;
    }

    private readonly Dictionary<long, Live> _live = new();
    // Chain run counts (and runs in a row without progress) for expeditions we started, by POI id.
    private readonly Dictionary<long, int> _chainRuns = new();
    private readonly Dictionary<long, int> _chainDry = new();
    private readonly List<Pending> _pending = new();
    // Last finished expedition per POI (kept even with repeating off), for manual repeats.
    private readonly Dictionary<long, ExpeditionPlan> _lastFinished = new();
    private ExpeditionGoals _lastGoals;
    private string _museum; // MuseumData key of the museum the state above belongs to
    // Helipad id -> when its helicopter was first seen Idle (since it last wasn't).
    private readonly Dictionary<long, float> _idleSince = new();
    private const float HelicopterSettleSeconds = 2f; // game time

    public RepeatExpeditions(Settings settings)
    {
        _settings = settings;
        ExpeditionActions.Claimed += OnClaimed;
    }

    public void Update()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollIntervalSeconds;

        var manager = ExpeditionManager.Instance;
        if (manager == null) { Reset(); return; } // not in a museum

        // Another museum loaded (possibly without a "no museum" moment in between): the old one's
        // expeditions vanish from the list, which must not read as "claimed".
        string museum = MuseumData.CurrentKey();
        if (museum != _museum)
        {
            Reset();
            _museum = museum;
            if (museum == null) return;
        }

        // Snapshot every expedition each poll: auto-claim (or the player) can claim a crate within
        // the same second it lands, so we can't wait to see the Reward stage.
        var active = new HashSet<long>();
        foreach (var kv in manager._expeditions)
        {
            var e = kv.Value;
            long poi = ((WorldMapLocationPOIDefinitionID)e.POI).ID;
            active.Add(poi);
            Snapshot(e, poi);
        }

        // Expeditions that are gone: claimed if they had come back, otherwise cancelled/failed.
        foreach (var poi in _live.Keys.Where(p => !active.Contains(p)).ToList())
        {
            var live = _live[poi];
            _live.Remove(poi);
            _chainRuns.Remove(poi);
            _chainDry.Remove(poi);
            if (live.Returning) OnFinished(live, live.Run + 1);
            else Log($"{live.Plan.DisplayName}: ended before returning (cancelled or failed), not repeating");
        }

        foreach (var pending in _pending.ToList())
            TryStart(pending, active);
    }

    private Live Snapshot(Expedition e, long poi)
    {
        if (!_live.TryGetValue(poi, out var live))
        {
            _live[poi] = live = new Live();
            long material = RepeatChoices.Stock(poi).Material; // seen before its crate is claimed
            if (material != 0) live.StockBefore = MaterialRewards.Stock(material);
        }
        live.Plan = ExpeditionPlan.From(e);
        LastLoadouts.Remember(live.Plan);
        live.Run = _chainRuns.GetValueOrDefault(poi);
        live.Dry = _chainDry.GetValueOrDefault(poi);
        live.Returning |= e.Stage is EExpeditionStage.Return or EExpeditionStage.Reward or EExpeditionStage.DropStaff;
        foreach (long perk in PerkRewards.InCrate(e)) live.Perks.Add(perk);
        return live;
    }

    /// <summary>
    /// Auto-claim can take a crate before our next poll sees it at the helipad: snapshot it (and
    /// the perks in it) at the moment it's claimed.
    /// </summary>
    private void OnClaimed(Expedition e)
    {
        if (ExpeditionManager.Instance == null) return;
        Snapshot(e, ((WorldMapLocationPOIDefinitionID)e.POI).ID).Returning = true;
    }

    private void OnFinished(Live live, int runsDone)
    {
        var plan = live.Plan;
        _lastFinished[plan.Poi.ID] = plan;
        if (!Repeating(plan)) return;

        // runsDone == 1: the player sent it and the mod never repeated it — log only, no notification.
        bool repeatedByUs = runsDone > 1;
        if (plan.Type == EExpeditionType.StudioRoom) { StudioFinished(plan, runsDone, repeatedByUs); return; }
        var perkGoal = RepeatChoices.PerkGoal(plan.Poi.ID);
        long perk = perkGoal.Perk;
        bool countPerk = perk != 0 && perkGoal.Amount > 0 && PerkRewards.Owned(perk) >= 0;
        var stock = RepeatChoices.Stock(plan.Poi.ID);
        string why;
        if (countPerk)
        {
            why = PerkStockReached(live, perk, perkGoal.Amount);
            // Reached: forget the target, so the next trip here runs once unless a new one is picked.
            if (PerkRewards.Owned(perk) >= perkGoal.Amount) RepeatChoices.SetPerk(plan.Poi.ID, 0);
        }
        else if (perk != 0)
        {
            why = PerkReached(live, perk);
            // Got it: forget the pick, so the next trip here doesn't go after another copy.
            if (live.Perks.Contains(perk)) RepeatChoices.SetPerk(plan.Poi.ID, 0);
        }
        else if (stock.Material != 0)
        {
            why = StockReached(plan, stock.Material, stock.Amount);
            // Reached: forget the target, so the next trip here runs once unless a new one is picked.
            if (MaterialRewards.Stock(stock.Material) >= stock.Amount) RepeatChoices.SetStock(plan.Poi.ID, 0, 0);
        }
        else
        {
            why = GoalsReached(plan);
            if (why == NoExhibits) { Log($"{plan.DisplayName}: no exhibits to collect here (materials/cash location), not repeating"); return; }
        }
        if (why != null) { Stop(plan, why, notify: repeatedByUs); return; }

        // The repeat limit. For an amount goal (a number of a perk, a stock target) it counts runs
        // in a row that brought none of it, so big amounts can finish but a rare drop still stops.
        int max = _settings.MaxRepeats.Value;
        int dry = 0;
        if (countPerk || stock.Material != 0)
        {
            bool progress = countPerk ? live.Perks.Contains(perk)
                : live.StockBefore < 0 || MaterialRewards.Stock(stock.Material) > live.StockBefore;
            dry = progress ? 0 : live.Dry + 1;
            if (dry >= max)
            {
                string what = countPerk ? PerkRewards.Name(perk) : $"more {MaterialRewards.Name(stock.Material)}";
                Stop(plan, $"{max} run{(max == 1 ? "" : "s")} in a row without {(countPerk ? "a " : "")}{what}", notify: repeatedByUs);
                return;
            }
        }
        else if (runsDone >= max + 1)
        {
            Stop(plan, $"reached the limit of {max} repeat{(max == 1 ? "" : "s")}", notify: repeatedByUs);
            return;
        }
        string stillNeeded = countPerk ? $"{perkGoal.Amount:N0} {PerkRewards.Name(perk)} (have {PerkRewards.Owned(perk):N0})"
            : perk != 0 ? $"the perk {PerkRewards.Shown(plan.Poi, perk)}"
            : stock.Material != 0 ? $"{stock.Amount:N0} {MaterialRewards.Name(stock.Material)} (have {MaterialRewards.Stock(stock.Material):N0})"
            : _lastGoals?.StillNeeded(_settings.UntilPristine.Value, _settings.UntilMaxKnowledge.Value) ?? "";
        _pending.Add(new Pending { Plan = plan, Run = runsDone, Dry = dry, StillNeeded = stillNeeded });
        Log($"{plan.DisplayName}: run {runsDone} done, repeat queued{(dry > 0 ? $" ({dry} in a row without progress)" : "")}");
    }

    private const string NoExhibits = "no exhibits to collect at this location";

    /// <summary>
    /// Whether finished runs here are repeated: art studio sessions by their own count (picked on
    /// the setup screen), expeditions by the location's repeat switch.
    /// </summary>
    private bool Repeating(ExpeditionPlan plan) => plan.Type switch
    {
        EExpeditionType.StudioRoom => RepeatChoices.StudioRuns(plan.Poi.ID) > 1,
        EExpeditionType.Expedition => RepeatChoices.IsOn(_settings, plan.Poi.ID),
        _ => false,
    };

    /// <summary>
    /// An art studio session ended: the player revealed its piece (the game doesn't let us claim
    /// it for them, see AutoClaim), so the next one is queued now, until the picked number of
    /// sessions has run. There are no goals to check: every session makes a piece.
    /// </summary>
    private void StudioFinished(ExpeditionPlan plan, int runsDone, bool repeatedByUs)
    {
        int runs = RepeatChoices.StudioRuns(plan.Poi.ID);
        if (runsDone >= runs)
        {
            RepeatChoices.SetStudioRuns(plan.Poi.ID, 1); // done: the next session here runs once unless picked again
            Stop(plan, $"made {runsDone} pieces", notify: repeatedByUs);
            return;
        }
        int left = runs - runsDone;
        _pending.Add(new Pending { Plan = plan, Run = runsDone, StillNeeded = $"{left} more piece{(left == 1 ? "" : "s")}" });
        Log($"{plan.DisplayName}: session {runsDone} of {runs} done, next one queued");
    }

    /// <summary>The perk goal: null while the perk hasn't come back yet; otherwise the reason to stop.</summary>
    private static string PerkReached(Live live, long perk)
    {
        var plan = live.Plan;
        string name = PerkRewards.Name(perk);
        string got = live.Perks.Count == 0 ? "no perk" : string.Join(", ", live.Perks.Select(PerkRewards.Name));
        var option = PerkRewards.For(plan).FirstOrDefault(o => o.Id == perk);
        string note = option == null ? " (this location can't drop it with this cargo)"
                    : !option.Available ? $" (it needs survey level {option.UnlockLevel})" : "";
        Log($"{plan.DisplayName}: crate had {got}; target {name}{note}"); // the log may name it; stop reasons are shown to the player
        string shown = PerkRewards.Shown(plan.Poi, perk);
        if (live.Perks.Contains(perk)) return $"got the perk {shown}";
        if (option == null) return $"{shown} can't drop here with this cargo";
        return null;
    }

    /// <summary>The perk count goal: null while the player has fewer than `amount`; otherwise the reason to stop.</summary>
    private static string PerkStockReached(Live live, long perk, int amount)
    {
        var plan = live.Plan;
        string name = PerkRewards.Name(perk); // only discovered perks can get a count goal, so it may be named
        int have = PerkRewards.Owned(perk);
        string got = live.Perks.Count == 0 ? "no perk" : string.Join(", ", live.Perks.Select(PerkRewards.Name));
        Log($"{plan.DisplayName}: crate had {got}; {name} owned {have:N0}, target {amount:N0}");
        if (have >= amount) return $"you have {have:N0} {name}";
        if (!PerkRewards.For(plan).Any(o => o.Id == perk)) return $"{name} can't drop here with this cargo";
        return null;
    }

    /// <summary>The stock goal: null while there's less than `amount` of the material; otherwise the reason to stop.</summary>
    private static string StockReached(ExpeditionPlan plan, long material, int amount)
    {
        string name = MaterialRewards.Name(material);
        int have = MaterialRewards.Stock(material);
        Log($"{plan.DisplayName}: {name} in stock {have:N0}, target {amount:N0}");
        if (have >= amount) return $"you have {have:N0} {name}";
        if (!MaterialRewards.For(plan).Any(m => m.Id == material)) return $"{name} can't drop here with this cargo";
        return null;
    }

    /// <summary>null while there's still something to get; otherwise the reason to stop.</summary>
    private string GoalsReached(ExpeditionPlan plan)
    {
        bool wantPristine = _settings.UntilPristine.Value, wantKnowledge = _settings.UntilMaxKnowledge.Value;
        var goals = ExpeditionGoals.Evaluate(plan, _settings.CountInventoryTowardsKnowledge.Value);
        _lastGoals = goals;
        if (goals.Exhibits.Count == 0) return NoExhibits; // repeats only collect exhibits
        if (!wantPristine && !wantKnowledge) return null; // no goals: only MaxRepeats stops it
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

        if (!Repeating(plan)) { Drop(p, null); return; } // switched off since
        if (activePois.Contains(poi)) { Drop(p, null); return; } // the player sent one there already

        string hardStop = HardStopReason(plan);
        if (hardStop != null) { Drop(p, hardStop); return; }

        var settings = plan.BuildSettings(out string problem);
        string wait = WaitReason(plan) ?? problem;
        if (wait == null && settings != null && _settings.AvoidSeasonalDangers.Value)
            wait = SeasonalDangers.Uncountered(settings);
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
        _chainDry[poi] = p.Dry;
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
    private string WaitReason(ExpeditionPlan plan)
    {
        var manager = ExpeditionManager.Instance;
        bool studio = plan.Type == EExpeditionType.StudioRoom;
        if (manager.TryGetExpeditionForItem(plan.Helipad, out _)) return studio ? "the studio is in use" : "the helipad is in use";
        if (studio) return StaffWaitReason(plan, manager); // no helicopter to wait for
        if (!plan.Helipad.TryGet(out var helipad)) return "the helipad is gone";
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        if (em.HasComponent<ECExpeditionItem>(helipad.Entity))
        {
            var state = em.GetComponentData<ECExpeditionItem>(helipad.Entity).State;
            if (state != EExpeditionItemState.Idle) { _idleSince.Remove(plan.Helipad.ID); return $"the helicopter is still busy ({state})"; }
            // After dropping the team off the helicopter is Idle for a moment (~0.7 s game time)
            // before it hovers and lands. A trip sent then came back before it had landed, and the
            // helicopter never dropped that team off (it hovered forever). Only a settled Idle
            // counts; game time, so a paused game doesn't count as settled.
            float now = UnityEngine.Time.time;
            if (!_idleSince.TryGetValue(plan.Helipad.ID, out float since)) _idleSince[plan.Helipad.ID] = since = now;
            if (now - since < HelicopterSettleSeconds) return "the helicopter is still busy (landing)";
        }

        return StaffWaitReason(plan, manager);
    }

    /// <summary>The team (busy, hurt, still resting from the studio) and the material a studio session uses.</summary>
    private string StaffWaitReason(ExpeditionPlan plan, ExpeditionManager manager)
    {
        var poiDef = plan.PoiDefinition;
        foreach (var id in plan.Staff)
        {
            if (!id.TryGet(out var staff)) continue;
            string name = ExpeditionPlan.StaffName(staff);
            if (manager.TryGetExpeditionsForStaff(id, out var list) && list.Count > 0) return $"{name} is still on an expedition";
            if (!ExpeditionUtils.IsStaffValidForPOI(poiDef, staff, out var effect))
                return $"{name} can't go yet ({(effect != null ? StudioRest.Name(effect) : "status effect")})";
            // The game lets artists back in the studio before they're fully rested, with a penalty.
            if (plan.Type == EExpeditionType.StudioRoom && _settings.WaitUntilArtistsRested.Value && StudioRest.Tiredness(staff) is string tired)
                return $"{name} is still resting ({tired})";
        }

        // Sculptures and statues use up a science material per session.
        if (plan.Type == EExpeditionType.StudioRoom && plan.ArtData.MaterialCost > 0)
        {
            long material = plan.ArtData.Material.ID;
            int have = MaterialRewards.Stock(material);
            if (have < plan.ArtData.MaterialCost)
                return $"not enough {MaterialRewards.Name(material)} ({have:N0} of {plan.ArtData.MaterialCost:N0})";
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

    private void Stop(ExpeditionPlan plan, string reason, bool notify = true)
    {
        Log($"{plan.DisplayName}: not repeating — {reason}");
        if (notify) Notify(NotifyKind.Stopped, $"Repeat stopped: {plan.DisplayName}", $"Not repeating: {reason}.");
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
        _chainDry.Clear();
        _lastFinished.Clear();
        _idleSince.Clear();
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
            var settings = plan.BuildSettings(out string problem);
            int cost = settings?.CalculateCost() ?? -1;
            lines.Add($"{plan.DisplayName} (stage {kv.Value.Stage}, cargo {plan.CargoModule?.name ?? "none"}, alternate={goals.UsesAlternateRewards}, pristinePossible={goals.PristinePossible})");
            lines.Add($"  goals: {goals.Describe()}");
            lines.Add($"  goals reached: {GoalsReached(plan) ?? "no"}");
            var target = RepeatChoices.PerkGoal(plan.Poi.ID);
            lines.Add($"  perks: {string.Join(", ", PerkRewards.For(plan).Select(o => $"{o.Name} (level {o.UnlockLevel}{(o.Available ? "" : ", not yet")}, owned {o.Owned})"))}" +
                      $"; in crate: {string.Join(", ", PerkRewards.InCrate(kv.Value).Select(PerkRewards.Name))}" +
                      $"; target: {(target.Perk == 0 ? "none" : $"{PerkRewards.Name(target.Perk)}{(target.Amount > 0 ? $" x{target.Amount}" : " (once)")}")}");
            var stockGoal = RepeatChoices.Stock(plan.Poi.ID);
            lines.Add($"  materials: {string.Join(", ", MaterialRewards.For(plan).Select(m => $"{m.Name} {m.Stock} (step {m.Step})"))}" +
                      $"; stock target: {(stockGoal.Material == 0 ? "none" : $"{stockGoal.Amount} {MaterialRewards.Name(stockGoal.Material)}")}");
            lines.Add($"  hard stop: {HardStopReason(plan) ?? "none"}; wait: {WaitReason(plan) ?? "none"}; build settings: {problem ?? "ok"}");
            lines.Add($"  seasonal: {(settings == null ? "n/a" : SeasonalDangers.Uncountered(settings) ?? "no uncountered dangers")}");
            lines.Add($"  cost {cost:N0}; money: {(cost >= 0 ? MoneyCheck.WhyNotAffordable(cost, _settings) ?? "ok" : "n/a")}; bills {MoneyCheck.MonthEndBills():N0}; projected profit {MoneyCheck.ProjectedProfit():N0}");
        }
        return lines.Count == 0 ? "no expeditions" : string.Join("\n", lines);
    }

    private static void Log(string message) => Entry.Host.Log.LogInfo($"[repeat] {message}");
}
