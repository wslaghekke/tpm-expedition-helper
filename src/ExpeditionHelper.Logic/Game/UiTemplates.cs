using System;
using System.Linq;
using TPS.Game.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Game widgets to clone on screens that don't have them, taken from the Settings menu prefab
/// (loaded once through the pause menu's own Addressables reference and kept loaded).
/// </summary>
internal static class UiTemplates
{
    private static GameObject _settingsPrefab;

    /// <summary>The Settings menu's "Disable Camera Shake" row: label + On/Off slider (WidgetToggleSlider).</summary>
    public static GameObject ToggleRow() => SettingsGamePanel()?._toggleDisableCameraShake?.gameObject;

    private static PreferencesPanelGame SettingsGamePanel()
    {
        if (_settingsPrefab == null)
        {
            try
            {
                var pause = Resources.FindObjectsOfTypeAll<PauseMenu>().FirstOrDefault(p => p.gameObject.scene.IsValid());
                if (pause == null) return null;
                _settingsPrefab = Addressables.LoadAssetAsync<GameObject>(pause._preferencesMenuRef.RuntimeKey).WaitForCompletion();
            }
            catch (Exception e)
            {
                Entry.Host.Log.LogError($"[ui] loading the settings menu prefab failed: {e.Message}");
                return null;
            }
        }
        return _settingsPrefab?.GetComponentInChildren<PreferencesPanelGame>(true);
    }
}
