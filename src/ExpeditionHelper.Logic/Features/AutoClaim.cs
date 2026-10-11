using System;
using System.Collections.Generic;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Claims returned expedition crates without the reward scene when the exhibit inside is of a type
/// the museum already has (the game's own `IsNewExhibit` flag is false): whole exhibits go straight
/// to inventory, pieces of multi-part (shard) exhibits are attached. New discoveries and non-exhibit
/// rewards are left for the player to open normally.
///
/// Polls instead of hooking: a handful of expeditions checked twice a second is negligible and
/// needs no native detour.
/// </summary>
internal sealed class AutoClaim
{
    private const float PollIntervalSeconds = 0.5f;

    private readonly Settings _settings;
    private float _nextPoll;

    // Claims are processed by the game on a later frame (GameEvents queue), so an expedition stays
    // at stage Reward briefly after we raise its claim. Remember what we already claimed.
    private readonly HashSet<IntPtr> _claimed = new();

    public AutoClaim(Settings settings) => _settings = settings;

    public void Update()
    {
        if (!_settings.AutoOpenCrates.Value) return;
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollIntervalSeconds;

        var manager = ExpeditionManager.Instance;
        if (manager == null) { _claimed.Clear(); return; } // not in a museum

        var waiting = new HashSet<IntPtr>();
        foreach (var kv in manager._expeditions)
        {
            var expedition = kv.Value;
            if (expedition.Stage != EExpeditionStage.Reward) continue;
            waiting.Add(expedition.Pointer);
            if (_claimed.Contains(expedition.Pointer)) continue;

            string decision = Decide(expedition, out bool claim, out bool toInventory);
            if (!claim) continue;

            string result = ExpeditionActions.Claim(expedition, toInventory);
            _claimed.Add(expedition.Pointer);
            Entry.Host.Log.LogInfo($"[auto-claim] {result} ({decision})");
        }
        _claimed.IntersectWith(waiting); // forget expeditions the game has finished with
    }

    /// <summary>Whether to auto-claim this crate, and a short human-readable reason.</summary>
    private string Decide(Expedition expedition, out bool claim, out bool toInventory)
    {
        claim = false;
        toInventory = false;

        var reward = expedition.Rewards?.Count > 0 ? expedition.Rewards[0] : null;
        var exhibit = reward?.TryCast<ExpeditionReward_Exhibit.Instance>();
        if (exhibit == null) return "not an exhibit";
        if (exhibit.IsNewExhibit) return "new discovery";

        var record = exhibit.ExhibitRecord;
        if (record.Definition != null && record.Definition.IsShardExhibit)
        {
            // A piece of a multi-part exhibit; the reward scene's only option is "Attach to Exhibit".
            if (!_settings.AttachShards.Value) return "shard attaching disabled";
            claim = true;
            return $"shard of {exhibit.DisplayName}, already discovered";
        }

        if (!_settings.SendKnownExhibitsToInventory.Value) return "sending to inventory disabled";
        if (!exhibit.CanMoveToInventory) return "can't go to inventory";
        claim = true;
        toInventory = true;
        return $"{record.ExhibitQuality}, already discovered";
    }
}
