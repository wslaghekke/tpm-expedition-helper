using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace ExpeditionHelper.Host;

/// <summary>
/// Everything the reloadable logic may use from the host. Subscriptions made through this
/// object are dropped automatically when the logic is unloaded, so a reload can never leave
/// stale handlers from an old logic version running.
/// </summary>
public sealed class HostApi
{
    public ManualLogSource Log { get; }
    public ConfigFile Config { get; }
    public string PluginDir { get; }

    /// <summary>Raised every frame on the main thread.</summary>
    public event Action Update
    {
        add { _update += value; _updateHandlers = null; }
        remove { _update -= value; _updateHandlers = null; }
    }

    private Action _update;
    private Delegate[] _updateHandlers; // cached invocation list: no allocation per frame

    /// <summary>Raised on the main thread when any config value changes (in game or by editing the .cfg).</summary>
    public event Action<SettingChangedEventArgs> ConfigChanged;

    /// <summary>Scratch space for the logic to expose objects to the dev bridge (e.g. `Host.Exports["state"]`).</summary>
    public Dictionary<string, object> Exports { get; } = new();

    internal HostApi(ManualLogSource log, ConfigFile config, string pluginDir)
    {
        Log = log;
        Config = config;
        PluginDir = pluginDir;
        Config.SettingChanged += (_, e) => ConfigChanged?.Invoke(e);
    }

    internal void RaiseUpdate()
    {
        if (_update == null) return;
        var handlers = _updateHandlers ??= _update.GetInvocationList();
        foreach (Action h in handlers)
        {
            try { h(); }
            catch (Exception e)
            {
                // Drop a handler that throws so one bug doesn't spam the log every frame.
                Update -= h;
                Log.LogError($"Update handler {h.Method.DeclaringType?.Name}.{h.Method.Name} threw and was removed: {e}");
            }
        }
    }

    internal void ResetSubscriptions()
    {
        _update = null;
        _updateHandlers = null;
        ConfigChanged = null;
        Exports.Clear();
    }
}
