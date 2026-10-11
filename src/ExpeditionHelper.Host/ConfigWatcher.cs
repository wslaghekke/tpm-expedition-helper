using System;
using System.IO;
using System.Threading;
using BepInEx.Configuration;

namespace ExpeditionHelper.Host;

/// <summary>Reloads the .cfg when it is edited on disk, so settings apply without a restart.</summary>
internal sealed class ConfigWatcher : IDisposable
{
    private readonly ConfigFile _config;
    private readonly HostApi _api;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private DateTime _lastWrite;

    public ConfigWatcher(ConfigFile config, HostApi api)
    {
        _config = config;
        _api = api;
        _debounce = new Timer(_ => MainThread.Post(ReloadIfChanged));
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(config.ConfigFilePath)!, Path.GetFileName(config.ConfigFilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        // Many editors (and sed -i) save atomically: write a temp file, then rename it over the original.
        _watcher.NotifyFilter |= NotifyFilters.FileName;
        _watcher.Changed += (_, _) => _debounce.Change(300, Timeout.Infinite);
        _watcher.Created += (_, _) => _debounce.Change(300, Timeout.Infinite);
        _watcher.Renamed += (_, _) => _debounce.Change(300, Timeout.Infinite);
        _lastWrite = SafeLastWrite();
    }

    private void ReloadIfChanged()
    {
        // BepInEx rewrites the file itself whenever a value is set from code; only react to real edits.
        var stamp = SafeLastWrite();
        if (stamp == _lastWrite) return;
        _lastWrite = stamp;
        try
        {
            _config.Reload();
            _lastWrite = SafeLastWrite();
            _api.Log.LogInfo("Config reloaded from disk");
        }
        catch (Exception e)
        {
            _api.Log.LogWarning($"Config reload failed: {e.Message}");
        }
    }

    private DateTime SafeLastWrite()
    {
        try { return File.GetLastWriteTimeUtc(_config.ConfigFilePath); }
        catch { return default; }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
