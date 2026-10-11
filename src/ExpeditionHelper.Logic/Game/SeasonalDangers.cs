using System.Collections.Generic;
using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Seasonal dangers a new run would meet, judged the way the expedition setup screen does:
/// for every month the expedition would span, the location's seasonal event is "Active" unless the
/// team/cargo counters it (WeightedExpeditionLogEntry.GetActiveType).
/// </summary>
internal static class SeasonalDangers
{
    /// <summary>null when no uncountered seasonal danger overlaps the run; otherwise a description.</summary>
    public static string Uncountered(ExpeditionSettings settings)
    {
        settings.GetDurationInMonths(out int start, out int end);
        int months = ((end - start) % 12 + 12) % 12; // the run can cross the new year
        var seen = new HashSet<string>();
        for (int i = 0; i <= months; i++)
        {
            int month = (start + i) % 12;
            var seasonal = settings.GetSeasonalEvent(month);
            var evt = seasonal?.SeasonalEvent;
            if (evt?.Event == null) continue;
            if (evt.Event.GetActiveType(settings) != LogEntryActiveType.Active) continue;

            string name = Translate(evt.Name) ?? "seasonal event";
            if (seen.Add(name)) return $"seasonal danger {name} ({seasonal.Start}–{seasonal.End}) that the team can't counter";
        }
        return null;
    }

    private static string Translate(TPS.Core.Localisation.LocalisedString s)
    {
        try { var t = s.Translation; return string.IsNullOrEmpty(t) ? null : t; }
        catch { return null; }
    }
}
