using System.Collections.Generic;
using System.Linq;
using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Everything needed to send an expedition again: captured from a live Expedition while its crate
/// waits on the helipad (after that the game removes it), and turned back into ExpeditionSettings
/// the same way the setup screen builds them.
/// </summary>
internal sealed class ExpeditionPlan
{
    public WorldMapLocationDefinitionID Location { get; private init; }
    public WorldMapLocationPOIDefinitionID Poi { get; private init; }
    public string PoiName { get; private init; } = "";
    public GameItemInstanceID Helipad { get; private init; }
    public List<StaffInstanceID> Staff { get; private init; } = new();
    public EExpeditionApproach Approach { get; private init; }
    public EExpeditionType Type { get; private init; }
    public ExpeditionBonusModuleID Cargo { get; private init; }

    public static ExpeditionPlan From(Expedition e) => new()
    {
        Location = e.Location,
        Poi = e.POI,
        PoiName = e.POI?.name ?? "?",
        Helipad = e.ItemID,
        Staff = e.Staff?.ToList() ?? new List<StaffInstanceID>(),
        Approach = e.Approach,
        Type = e.ExpeditionType,
        Cargo = e.CargoModule,
    };

    /// <summary>From the setup screen's settings (before sending; the team isn't captured).</summary>
    public static ExpeditionPlan From(ExpeditionSettings s) => new()
    {
        Location = s.Location,
        Poi = s.Definition,
        PoiName = s.Definition?.name ?? "?",
        Helipad = s.ItemID,
        Approach = s.Approach,
        Type = s.ExpeditionType,
        Cargo = s.CargoModule,
    };

    public WorldMapLocationPOIDefinition PoiDefinition => LevelDatabaseUtils.Get(Poi);

    public WorldMapLocationPOI PoiInstance =>
        WorldMap.Instance != null && WorldMap.Instance.GetWorldMapLocationPOI(Poi, out var poi) ? poi : null;

    public ExpeditionBonusModule CargoModule =>
        Cargo == ExpeditionBonusModuleID.Null ? null : LevelDatabaseUtils.Get(Cargo);

    /// <summary>Build settings for a new run, or null with a reason.</summary>
    public ExpeditionSettings BuildSettings(out string problem)
    {
        problem = null;
        var location = LevelDatabaseUtils.Get(Location);
        var poi = PoiInstance;
        if (location == null || poi == null) { problem = "location not available"; return null; }

        var settings = new ExpeditionSettings(location, poi, Helipad)
        {
            Approach = Approach,
            ExpeditionType = Type,
            CargoModule = Cargo,
        };

        // Fill each required slot with a team member that fits it (as the team-select menu does).
        var unused = new List<StaffInstance>();
        foreach (var id in Staff)
            if (id.TryGet(out var s)) unused.Add(s);
        foreach (var slot in settings.AssignedStaff)
        {
            var match = unused.FirstOrDefault(s => slot.RequiredStaff.IsValidForExpedition(s));
            if (match == null) { problem = "the team no longer fits the required staff slots"; return null; }
            slot.Staff = match;
            unused.Remove(match);
        }
        if (!settings.AreAllStaffAssigned()) { problem = "not all staff slots could be filled"; return null; }
        return settings;
    }

    public static string StaffName(StaffInstance staff)
    {
        // ToString() is "Staff_47289: Kate Chiptune (Expert)"; there's no plain name property.
        string s = staff.ToString() ?? "";
        int colon = s.IndexOf(": ");
        int paren = s.LastIndexOf(" (");
        if (colon >= 0) s = paren > colon ? s[(colon + 2)..paren] : s[(colon + 2)..];
        return s;
    }

    /// <summary>The names the world map shows, e.g. "Bone Belt – Hermit Hills".</summary>
    public string DisplayName
    {
        get
        {
            try
            {
                string location = LevelDatabaseUtils.Get(Location)?.Name.Translation;
                string poi = PoiDefinition?.Name.Translation;
                if (!string.IsNullOrEmpty(location) && !string.IsNullOrEmpty(poi)) return $"{location} – {poi}";
                if (!string.IsNullOrEmpty(poi)) return poi;
            }
            catch { /* fall back to the asset name */ }
            return PoiName.Replace("LocationPOI_", "").Replace('_', ' ');
        }
    }
}
