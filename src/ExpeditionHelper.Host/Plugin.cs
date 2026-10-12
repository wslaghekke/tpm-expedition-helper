using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace ExpeditionHelper.Host;

/// <summary>
/// Permanent entry point. Everything here is loaded once per game session; gameplay code
/// lives in ExpeditionHelper.Logic, which <see cref="LogicLoader"/> hot-reloads.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "tpm.expeditionhelper";
    public const string Name = "Expedition Helper";
    public const string Version = "0.2.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Logger { get; private set; } = null!;

    internal HostApi Api { get; private set; } = null!;
    internal LogicLoader Loader { get; private set; } = null!;
    private ConfigWatcher _configWatcher;

    public override void Load()
    {
        Instance = this;
        Logger = Log;

        string pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;
        Api = new HostApi(Log, Config, pluginDir);
        Loader = new LogicLoader(Api, Path.Combine(pluginDir, "reload"));
        _configWatcher = new ConfigWatcher(Config, Api);

        // No ClassInjector here: Denuvo breaks Il2CppInterop's type injection on this game.
        // FrameTick drives the main-thread queue and HostApi.Update instead.
        FrameTick.Install();

#if DEV_BRIDGE
        Dev.DevBridge.Start(Api, Loader);
#endif

        // Defer the first logic load to the first frame so Unity is fully up.
        MainThread.Post(() => Loader.Reload("startup"));
        Log.LogInfo($"{Name} {Version} host loaded");
    }

    public override bool Unload()
    {
        Loader.Unload();
        _configWatcher?.Dispose();
        return true;
    }
}
