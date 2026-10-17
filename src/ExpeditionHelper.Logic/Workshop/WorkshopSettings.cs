using BepInEx.Configuration;

namespace ExpeditionHelper.Logic.Workshop;

/// <summary>
/// Settings for the Workshop tweaks, in their own [Workshop] section. Kept apart from the
/// expedition settings so the Workshop tweaks can move to a plugin of their own later.
/// </summary>
public sealed class WorkshopSettings
{
    public ConfigEntry<bool> WiderProjectMenu { get; }

    public ConfigEntryBase[] Entries => new ConfigEntryBase[] { WiderProjectMenu };

    public WorkshopSettings(ConfigFile cfg)
    {
        WiderProjectMenu = cfg.Bind("Workshop", "WiderProjectMenu", false,
            "Make the Start Workshop Project menu wider, so all category tabs fit and projects show in 3 columns instead of 2.");
    }
}
