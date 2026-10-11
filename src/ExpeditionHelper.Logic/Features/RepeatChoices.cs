using System.Collections.Generic;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Whether to repeat expeditions to a location: the player's choice on the setup screen, or the
/// global default from the settings. Kept in memory only (resets on restart / logic reload).
/// </summary>
internal static class RepeatChoices
{
    private static readonly Dictionary<long, bool> ByPoi = new();

    public static bool IsOn(Settings settings, long poi) =>
        ByPoi.TryGetValue(poi, out bool on) ? on : settings.RepeatExpeditions.Value;

    public static void Set(long poi, bool on) => ByPoi[poi] = on;
}
