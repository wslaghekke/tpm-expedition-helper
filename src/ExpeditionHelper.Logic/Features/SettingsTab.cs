using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using ExpeditionHelper.Logic.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// The "Expedition Helper" tab in the game's Settings menu. It edits the same ConfigEntries as
/// the .cfg file (BepInEx saves every change), so both stay in sync.
/// </summary>
internal static class SettingsTab
{
    private static readonly int[] CashReserves = { 0, 5_000, 10_000, 25_000, 50_000, 100_000, 250_000 };
    private static readonly int[] RepeatLimits = { 1, 2, 3, 5, 10, 15, 20, 30, 50 };

    public static void Install(Settings s)
    {
        SettingsMenuTab.Install("Expedition Helper", "Icons_Menu_TitleHeaders_ExpeditionMap",
            page => Build(page, s), () => ResetToDefaults(s));
    }

    private static void Build(SettingsPage page, Settings s)
    {
        bool AutoOpen() => s.AutoOpenCrates.Value;
        bool Repeat() => s.RepeatExpeditions.Value;

        page.Header("Returned expedition crates");
        Toggle(page, "Open crates automatically", s.AutoOpenCrates);
        Toggle(page, "Send known exhibits to inventory", s.SendKnownExhibitsToInventory, AutoOpen);
        Toggle(page, "Attach pieces of known exhibits", s.AttachShards, AutoOpen);
        Toggle(page, "Claim science materials", s.AutoClaimMaterials, AutoOpen);
        Toggle(page, "Claim expert XP", s.AutoClaimExpertXP, AutoOpen);

        page.Header("Repeat expeditions");
        Toggle(page, "Repeat until done by default", s.RepeatExpeditions);
        Toggle(page, "Until a pristine copy is owned", s.UntilPristine, Repeat);
        Toggle(page, "Until knowledge is maxed", s.UntilMaxKnowledge, Repeat);
        Toggle(page, "Count inventory towards knowledge", s.CountInventoryTowardsKnowledge, () => Repeat() && s.UntilMaxKnowledge.Value);
        Toggle(page, "Wait out seasonal dangers", s.AvoidSeasonalDangers, Repeat);
        Steps(page, "Maximum repeats in a row", s.MaxRepeats, RepeatLimits, v => v.ToString(CultureInfo.InvariantCulture), Repeat);
        EnumChoice(page, "Notifications", s.Notifications, new Dictionary<NotificationLevel, string>
        {
            [NotificationLevel.All] = "Waiting, sent and stopped",
            [NotificationLevel.StopsOnly] = "Only when stopped",
            [NotificationLevel.None] = "None",
        }, Repeat);

        page.Header("Money for repeats");
        Steps(page, "Keep at least this much cash", s.MinCashReserve, CashReserves, v => v.ToString("N0", CultureInfo.InvariantCulture), Repeat);
        Toggle(page, "Only while the museum makes a profit", s.OnlyWhenProfitable, Repeat);
        Toggle(page, "Keep month-end costs covered", s.KeepMonthlyCostsCovered, Repeat);
    }

    private static void Toggle(SettingsPage page, string label, ConfigEntry<bool> entry, Func<bool> enabled = null) =>
        page.Toggle(label, entry.Description.Description, () => entry.Value, v => entry.Value = v, enabled);

    /// <summary>A dropdown of preset values; a custom value from the .cfg is shown as an extra choice.</summary>
    private static void Steps(SettingsPage page, string label, ConfigEntry<int> entry, int[] presets, Func<int, string> format, Func<bool> enabled)
    {
        int[] Values() => presets.Contains(entry.Value) ? presets : presets.Append(entry.Value).OrderBy(v => v).ToArray();
        page.Choice(label, entry.Description.Description,
            () => Values().Select(format).ToArray(),
            () => Array.IndexOf(Values(), entry.Value),
            i => { var values = Values(); if (i >= 0 && i < values.Length) entry.Value = values[i]; },
            enabled);
    }

    private static void EnumChoice<T>(SettingsPage page, string label, ConfigEntry<T> entry, Dictionary<T, string> names, Func<bool> enabled) where T : struct, Enum
    {
        var values = names.Keys.ToArray();
        page.Choice(label, entry.Description.Description,
            () => values.Select(v => names[v]).ToArray(),
            () => Array.IndexOf(values, entry.Value),
            i => { if (i >= 0 && i < values.Length) entry.Value = values[i]; },
            enabled);
    }

    private static void ResetToDefaults(Settings s)
    {
        foreach (var entry in s.Entries) entry.BoxedValue = entry.DefaultValue;
    }
}
