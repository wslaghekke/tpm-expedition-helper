using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>Actions on expeditions, done the same way the game's own UI does them.</summary>
internal static class ExpeditionActions
{
    /// <summary>
    /// Claim a returned expedition's crate without the reward scene. The reward scene's claim
    /// buttons end in TransitionBackToLevel, which raises RewardClaimed; ExpeditionManager.OnRewardClaimed
    /// does the work when the event is processed. `moveToInventory` = true is "Send to inventory",
    /// false is the other button ("Place Exhibit", "Attach to Exhibit", ...).
    /// </summary>
    public static string Claim(Expedition expedition, bool moveToInventory)
    {
        if (expedition == null) return "no expedition";
        if (expedition.Stage != EExpeditionStage.Reward) return $"stage is {expedition.Stage}, not Reward";
        var reward = expedition.Rewards?.Count > 0 ? expedition.Rewards[0] : null;
        if (reward == null) return "expedition has no reward";
        if (moveToInventory && !reward.CanMoveToInventory) return $"'{reward.DisplayName}' can't be moved to inventory";

        // Expert XP crates roll their amount in Calculate(), which the reward scene calls when it
        // shows the crate; Claim then hands out _calculatedXP. Without the scene it stays 0 and the
        // team gets nothing (verified: crate 0 -> 350 after Calculate, then +350 on claim).
        // It's the only reward type with a Calculate step.
        var xp = reward.TryCast<ExpeditionReward_ExpertXP.Instance>();
        if (xp != null && xp._calculatedXP <= 0) xp.Calculate();

        GameEventsUtil.Raise(new GameEvent.ExpeditionEvent.RewardClaimed
        {
            Expedition = expedition,
            Reward = reward,
            MoveToInventory = moveToInventory,
        });
        return moveToInventory
            ? $"sent '{reward.DisplayName}' from {expedition.POI?.name} to inventory"
            : $"claimed '{reward.DisplayName}' from {expedition.POI?.name} ({reward.ClaimButtonText})";
    }

    /// <summary>First expedition waiting at its helipad with a reward that can go to inventory.</summary>
    public static Expedition FirstClaimable()
    {
        var em = ExpeditionManager.Instance;
        if (em == null) return null;
        foreach (var kv in em._expeditions)
        {
            var e = kv.Value;
            if (e.Stage == EExpeditionStage.Reward && e.Rewards?.Count > 0 && e.Rewards[0].CanMoveToInventory)
                return e;
        }
        return null;
    }
}
