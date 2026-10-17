using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// How rested an artist is after an art studio session (verified in game). The session leaves
/// "Blank Canvas" (StudioRoomExhaustionStatusEffect, 30 days: the game won't send them to the
/// studio), which ends in "Blank-ish Canvas" (30 days: they may go, but the session setup gets a
/// Creative Juice-less event). The second is found as the first one's StatusEffectsOnEnd rather
/// than by name.
/// </summary>
internal static class StudioRest
{
    /// <summary>The feeling an artist still has from a past session ("Blank-ish Canvas"), or null when fully rested.</summary>
    public static string Tiredness(StaffInstance staff)
    {
        var studio = TPS.Core.StaticGameSystem<StudioManager>.Instance;
        var heavy = studio != null ? LevelDatabaseUtils.Get(studio.Config.StudioRoomExhaustionStatusEffect) : null;
        if (heavy == null) return null;

        var effects = staff.Cast<CharacterInstance>().Modifiers?._statusEffectDefinitions;
        if (effects == null) return null;
        foreach (var effect in effects)
        {
            if (effect == null) continue;
            if (effect.ID == heavy.ID) return Name(effect);
            foreach (var next in heavy.StatusEffectsOnEnd)
                if (effect.ID == next.ID) return Name(effect);
        }
        return null;
    }

    /// <summary>A status effect's name as the game shows it ("Blank Canvas"), or its asset name.</summary>
    public static string Name(CharacterStatusEffectBaseDefinition effect)
    {
        try
        {
            string shown = effect.LocalisedNameMale.Translation;
            if (!string.IsNullOrEmpty(shown)) return shown;
        }
        catch { /* fall back to the asset name */ }
        return effect.name.Replace("StatusEffect_", "");
    }
}
