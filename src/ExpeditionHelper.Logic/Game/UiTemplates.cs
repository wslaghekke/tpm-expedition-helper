using System;
using System.Linq;
using TPS.Game.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Game widgets to clone on screens that don't have them, taken from the Settings menu prefab
/// (loaded through the pause menu's own Addressables reference and kept loaded).
///
/// Loading it on first use stalled the first setup screen for ~200 ms (a full-scene search for
/// the pause menu plus a synchronous prefab load), so <see cref="Preload"/> starts an async load
/// as soon as a museum is up; <see cref="ToggleRow"/> only waits if that hasn't finished yet.
/// </summary>
internal static class UiTemplates
{
    private static AsyncOperationHandle<GameObject> _load;
    private static bool _loadStarted;
    private static GameObject _settingsPrefab;

    /// <summary>Start loading the templates in the background, if a museum is loaded. Cheap to call again.</summary>
    public static void Preload()
    {
        if (_loadStarted) return;
        try
        {
            // One full-object search (~35 ms), done once per session at a calm moment.
            var pause = Resources.FindObjectsOfTypeAll<PauseMenu>().FirstOrDefault(p => p.gameObject.scene.IsValid());
            if (pause == null) return; // main menu: try again once a museum is loaded
            _load = Addressables.LoadAssetAsync<GameObject>(pause._preferencesMenuRef.RuntimeKey);
            _loadStarted = true;
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[ui] preloading the settings menu prefab failed: {e.Message}");
            _loadStarted = true; // don't retry every time
        }
    }

    /// <summary>The Settings menu's "Disable Camera Shake" row: label + On/Off slider (WidgetToggleSlider).</summary>
    public static GameObject ToggleRow() => SettingsGamePanel()?._toggleDisableCameraShake?.gameObject;

    private static PreferencesPanelGame SettingsGamePanel()
    {
        if (_settingsPrefab == null)
        {
            Preload();
            if (!_load.IsValid()) return null;
            try { _settingsPrefab = _load.WaitForCompletion(); } // instant once the background load is done
            catch (Exception e)
            {
                Entry.Host.Log.LogError($"[ui] loading the settings menu prefab failed: {e.Message}");
                return null;
            }
        }
        return _settingsPrefab?.GetComponentInChildren<PreferencesPanelGame>(true);
    }
}
