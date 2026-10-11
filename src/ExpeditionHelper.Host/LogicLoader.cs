using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using HarmonyLib;

namespace ExpeditionHelper.Host;

/// <summary>
/// Loads ExpeditionHelper.Logic.dll from the reload folder and swaps it whenever a new build
/// lands there.
///
/// Each version gets its own non-collectible AssemblyLoadContext: Harmony/MonoMod can't
/// reliably patch from collectible contexts, so old versions are left in memory (a few hundred
/// KB per reload, fine for a dev session). Dependencies resolve from the default context, so the
/// logic shares the host, BepInEx and game interop assemblies.
/// </summary>
public sealed class LogicLoader
{
    public const string LogicAssemblyName = "ExpeditionHelper.Logic";
    public const string EntryTypeName = "ExpeditionHelper.Logic.Entry";
    public const string HarmonyId = "tpm.expeditionhelper.logic";

    private readonly HostApi _api;
    private readonly string _dir;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private MethodInfo _unload;
    private int _generation;

    public Assembly Current { get; private set; }
    public DateTime LoadedAt { get; private set; }
    public string LastError { get; private set; }
    public int Generation => _generation;

    public LogicLoader(HostApi api, string dir)
    {
        _api = api;
        _dir = dir;
        Directory.CreateDirectory(dir);
        _debounce = new Timer(_ => MainThread.Post(() => Reload("file change")));
        _watcher = new FileSystemWatcher(dir, LogicAssemblyName + ".dll")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        // MSBuild writes dll then pdb; wait for writes to settle.
        FileSystemEventHandler kick = (_, _) => _debounce.Change(500, Timeout.Infinite);
        _watcher.Changed += kick;
        _watcher.Created += kick;
        _watcher.Renamed += (_, _) => _debounce.Change(500, Timeout.Infinite);
    }

    /// <summary>Must be called on the main thread.</summary>
    public bool Reload(string reason)
    {
        string dllPath = Path.Combine(_dir, LogicAssemblyName + ".dll");
        if (!File.Exists(dllPath))
        {
            LastError = $"No logic build at {dllPath}";
            _api.Log.LogWarning(LastError);
            return false;
        }

        Unload();

        try
        {
            byte[] dll = ReadShared(dllPath);
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            byte[] pdb = File.Exists(pdbPath) ? ReadShared(pdbPath) : null;

            int gen = Interlocked.Increment(ref _generation);
            var alc = new LogicLoadContext($"{LogicAssemblyName}#{gen}");
            using var dllStream = new MemoryStream(dll);
            using var pdbStream = pdb != null ? new MemoryStream(pdb) : null;
            var asm = alc.LoadFromStream(dllStream, pdbStream);

            var entry = asm.GetType(EntryTypeName, throwOnError: true)!;
            var load = entry.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)
                       ?? throw new MissingMethodException(EntryTypeName, "Load");
            _unload = entry.GetMethod("Unload", BindingFlags.Public | BindingFlags.Static);

            load.Invoke(null, new object[] { _api });

            Current = asm;
            LoadedAt = DateTime.Now;
            LastError = null;
            _api.Log.LogInfo($"Logic #{gen} loaded ({reason})");
            return true;
        }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
            LastError = inner.ToString();
            _api.Log.LogError($"Logic load failed ({reason}): {inner}");
            Unload();
            return false;
        }
    }

    /// <summary>Must be called on the main thread.</summary>
    public void Unload()
    {
        if (_unload != null)
        {
            try { _unload.Invoke(null, null); }
            catch (Exception e) { _api.Log.LogError($"Logic Unload threw: {e.InnerException ?? e}"); }
        }
        _unload = null;

        // Belt and braces: whatever the logic forgot to clean up.
        try { Harmony.UnpatchID(HarmonyId); }
        catch (Exception e) { _api.Log.LogError($"Unpatching {HarmonyId} failed: {e}"); }
        _api.ResetSubscriptions();
        DropLogicTypedConfigEntries();
        Current = null;
    }

    /// <summary>
    /// ConfigEntry&lt;T&gt; where T is defined in the logic (e.g. an enum) is tied to that logic
    /// version's copy of T; the next version's Bind would fail its cast. Drop those entries and
    /// re-read the file so the next Bind picks up the persisted value as an orphaned entry.
    /// </summary>
    private void DropLogicTypedConfigEntries()
    {
        var cfg = _api.Config;
        var stale = cfg.Keys.Where(k => GetLoadContext(cfg[k].SettingType.Assembly) is LogicLoadContext).ToList();
        if (stale.Count == 0) return;
        try
        {
            // No Save() here: it would overwrite edits made to the .cfg on disk that the watcher
            // hasn't applied yet. SaveOnConfigSet already persisted every in-game change.
            foreach (var def in stale) cfg.Remove(def);
            cfg.Reload();
        }
        catch (Exception e)
        {
            _api.Log.LogError($"Dropping logic-typed config entries failed: {e}");
        }
    }

    private static AssemblyLoadContext GetLoadContext(Assembly asm) => AssemblyLoadContext.GetLoadContext(asm);

    /// <summary>
    /// Resolves references to already-loaded assemblies (host, BepInEx, interop) so the logic
    /// shares their types instead of getting a second copy that would break type identity.
    /// </summary>
    private sealed class LogicLoadContext : AssemblyLoadContext
    {
        public LogicLoadContext(string name) : base(name, isCollectible: false) { }

        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name == LogicAssemblyName) return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || GetLoadContext(asm) is LogicLoadContext) continue;
                if (string.Equals(asm.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase)) return asm;
            }
            return null; // fall back to the default context's resolution
        }
    }

    private static byte[] ReadShared(string path)
    {
        // The build may still hold the file briefly; retry a few times.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(100);
            }
        }
    }
}
