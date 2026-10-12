using BepInEx.Configuration;

namespace ExpeditionHelper.Logic;

public enum NotificationLevel
{
    /// <summary>When a repeat starts waiting, is sent, or stops.</summary>
    All,
    /// <summary>Only when a repeat stops for good.</summary>
    StopsOnly,
    None,
}

/// <summary>
/// Settings live in BepInEx/config/tpm.expeditionhelper.cfg. Editing that file while the
/// game runs applies immediately.
/// </summary>
public sealed class Settings
{
    // [Expeditions] auto-open
    public ConfigEntry<bool> AutoOpenCrates { get; }
    public ConfigEntry<bool> SendKnownExhibitsToInventory { get; }
    public ConfigEntry<bool> AttachShards { get; }
    public ConfigEntry<bool> AutoClaimMaterials { get; }
    public ConfigEntry<bool> AutoClaimExpertXP { get; }
    public ConfigEntry<bool> AutoClaimPerks { get; }

    // [Repeat]
    public ConfigEntry<bool> RepeatExpeditions { get; }
    public ConfigEntry<bool> UntilPristine { get; }
    public ConfigEntry<bool> UntilMaxKnowledge { get; }
    public ConfigEntry<bool> CountInventoryTowardsKnowledge { get; }
    public ConfigEntry<int> MinCashReserve { get; }
    public ConfigEntry<bool> OnlyWhenProfitable { get; }
    public ConfigEntry<bool> KeepMonthlyCostsCovered { get; }
    public ConfigEntry<bool> AvoidSeasonalDangers { get; }
    public ConfigEntry<int> MaxRepeats { get; }
    public ConfigEntry<NotificationLevel> Notifications { get; }

    public ConfigEntryBase[] Entries => new ConfigEntryBase[]
    {
        AutoOpenCrates, SendKnownExhibitsToInventory, AttachShards, AutoClaimMaterials, AutoClaimExpertXP, AutoClaimPerks,
        RepeatExpeditions, UntilPristine, UntilMaxKnowledge, CountInventoryTowardsKnowledge, MinCashReserve,
        OnlyWhenProfitable, KeepMonthlyCostsCovered, AvoidSeasonalDangers, MaxRepeats, Notifications,
    };

    public Settings(ConfigFile cfg)
    {
        AutoOpenCrates = cfg.Bind("Expeditions", "AutoOpenCrates", false,
            "Automatically open expedition reward crates when they arrive.");
        SendKnownExhibitsToInventory = cfg.Bind("Expeditions", "SendKnownExhibitsToInventory", true,
            "When auto-opening, send exhibits of an already-unlocked type straight to inventory. New discoveries still show the normal reveal.");
        AttachShards = cfg.Bind("Expeditions", "AttachShards", true,
            "When auto-opening, attach pieces of multi-part (shard) exhibits you've already discovered, like the reward screen's \"Attach to Exhibit\".");

        AutoClaimMaterials = cfg.Bind("Expeditions", "AutoClaimMaterials", true,
            "When auto-opening, also claim crates with science materials.");
        AutoClaimExpertXP = cfg.Bind("Expeditions", "AutoClaimExpertXP", true,
            "When auto-opening, also claim crates with expert XP.");
        AutoClaimPerks = cfg.Bind("Expeditions", "AutoClaimPerks", true,
            "When auto-opening, also claim crates with perks (they go to your perks, ready to install).");

        RepeatExpeditions = cfg.Bind("Repeat", "RepeatExpeditions", false,
            "Default for the \"Repeat until done\" toggle on the expedition setup screen: send a finished expedition again (same location, team and cargo) until every enabled goal below is reached for ALL exhibits that location can drop. The toggle overrides this per location (until the game restarts).");
        UntilPristine = cfg.Bind("Repeat", "UntilPristine", true,
            "Goal: the museum owns a pristine copy (placed or in inventory) of every exhibit the location can drop.");
        UntilMaxKnowledge = cfg.Bind("Repeat", "UntilMaxKnowledge", true,
            "Goal: knowledge is maxed for every exhibit the location can drop.");
        CountInventoryTowardsKnowledge = cfg.Bind("Repeat", "CountInventoryTowardsKnowledge", true,
            "For the knowledge goal, count the insight you'd get by analysing exhibits already in your inventory.");
        MinCashReserve = cfg.Bind("Repeat", "MinCashReserve", 10000,
            "Don't repeat when museum cash would drop below this after paying the expedition cost.");
        OnlyWhenProfitable = cfg.Bind("Repeat", "OnlyWhenProfitable", true,
            "Hold off repeating while the museum is losing money (negative profit); resume once it's profitable again.");
        KeepMonthlyCostsCovered = cfg.Bind("Repeat", "KeepMonthlyCostsCovered", true,
            "Hold off repeating if paying the expedition would leave too little cash to cover the upcoming month-end costs (salaries, loans, ...).");
        AvoidSeasonalDangers = cfg.Bind("Repeat", "AvoidSeasonalDangers", true,
            "Hold a repeat while the run would overlap a seasonal danger the team and cargo can't counter; it goes once the season has passed.");
        MaxRepeats = cfg.Bind("Repeat", "MaxRepeats", 10,
            "Safety limit: stop after this many automatic repeats of the same expedition in a row.");
        Notifications = cfg.Bind("Repeat", "Notifications", NotificationLevel.All,
            "In-game notifications about repeats. All: when a repeat starts waiting, is sent, or stops. StopsOnly: only when it stops. None: log only.");
    }
}
