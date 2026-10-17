using System;
using System.Collections.Generic;
using ExpeditionHelper.Logic.Game;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Claims returned expedition crates without the reward scene when the exhibit inside is of a type
/// the museum already has (the game's own `IsNewExhibit` flag is false): whole exhibits go straight
/// to inventory, pieces of multi-part (shard) exhibits are attached; science materials, expert XP
/// and perks are claimed if enabled. New discoveries, art studio pieces and other rewards are left
/// for the player to open.
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
    private readonly HashSet<IntPtr> _skipped = new(); // crates we decided to leave, logged once

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
            if (!claim)
            {
                if (_skipped.Add(expedition.Pointer)) Entry.Host.Log.LogInfo($"[auto-claim] left for you: {expedition.POI?.name} ({decision})");
                continue;
            }

            string result = ExpeditionActions.Claim(expedition, toInventory);
            _claimed.Add(expedition.Pointer);
            Entry.Host.Log.LogInfo($"[auto-claim] {result} ({decision})");
        }
        _claimed.IntersectWith(waiting); // forget expeditions the game has finished with
        _skipped.IntersectWith(waiting);
    }

    /// <summary>
    /// Non-exhibit rewards: their reward screen has a single claim button (same event with
    /// MoveToInventory = false). Only the kinds enabled in the settings are claimed.
    /// </summary>
    private string DecideOther(IExpeditionRewardInstance reward, out bool claim)
    {
        claim = false;
        // e.g. "TPS.Game.ExpeditionReward_ScienceMaterial+Instance"
        string type = reward.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName ?? "";
        if (type.Contains("ExpeditionReward_ScienceMaterial"))
        {
            if (!_settings.AutoClaimMaterials.Value) return "materials (auto-claim off)";
            claim = true;
            return "science materials";
        }
        if (type.Contains("ExpeditionReward_ExpertXP"))
        {
            if (!_settings.AutoClaimExpertXP.Value) return "expert XP (auto-claim off)";
            claim = true;
            return "expert XP";
        }
        if (type.Contains("ExpeditionReward_Perk"))
        {
            if (!_settings.AutoClaimPerks.Value) return "perk (auto-claim off)";
            claim = true;
            return "perk";
        }
        return $"not an exhibit ({type})";
    }

    /// <summary>Whether to auto-claim this crate, and a short human-readable reason.</summary>
    private string Decide(Expedition expedition, out bool claim, out bool toInventory)
    {
        claim = false;
        toInventory = false;

        // Art studio sessions end in the same Reward stage, but the game opens their reveal by
        // itself; claiming the piece at that moment took the reward from under the reveal and the
        // game crashed.
        if (expedition.ExpeditionType != EExpeditionType.Expedition) return "art studio piece";

        var reward = expedition.Rewards?.Count > 0 ? expedition.Rewards[0] : null;
        if (reward == null) return "no reward";
        var exhibit = reward.TryCast<ExpeditionReward_Exhibit.Instance>();
        if (exhibit == null) return DecideOther(reward, out claim);
        if (exhibit.IsNewExhibit) return "new discovery";

        var record = exhibit.ExhibitRecord;
        if (record.Definition != null && record.Definition.IsShardExhibit && !exhibit.CanMoveToInventory)
        {
            // A piece of a multi-part exhibit; the reward scene's only option is "Attach to Exhibit".
            // (The piece that completes the exhibit instead offers "Place Exhibit" / "Send to
            // inventory" and CanMoveToInventory — that one is handled like a whole exhibit below;
            // claiming it without inventory would put it in the player's cursor to place.)
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
