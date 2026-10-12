using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Keeps the per-location choices (<see cref="RepeatChoices"/>) and last teams (<see cref="LastLoadouts"/>)
/// across game restarts, in one file per museum under BepInEx/config/ExpeditionHelper/museums/.
///
/// A save slot holds several museums (one level save each), each with its own staff, so the key is
/// the loaded museum's playthrough (saved with it): game mode, level id, playthrough id and the
/// date that playthrough started. Changes are written at most once a second, from <see cref="Update"/>.
///
/// File format, one entry per line ("#" lines are comments):
///   repeat &lt;poi&gt; on|off
///   perk &lt;poi&gt; &lt;perk id&gt;
///   stock &lt;poi&gt; &lt;material id&gt; &lt;amount&gt;
///   team &lt;poi&gt; &lt;cargo id&gt; &lt;staff id&gt;,&lt;staff id&gt;,...
/// </summary>
internal static class MuseumData
{
    private const float CheckIntervalSeconds = 1f;

    private static string _key;   // null while no museum is loaded
    private static bool _dirty;
    private static float _nextCheck;

    /// <summary>Something to store changed; it's written on the next <see cref="Update"/>.</summary>
    public static void Changed() => _dirty = true;

    public static void Update()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextCheck) return;
        _nextCheck = now + CheckIntervalSeconds;

        string key = CurrentKey();
        if (key != _key)
        {
            Flush();
            _key = key;
            RepeatChoices.Clear();
            LastLoadouts.Clear();
            _dirty = false;
            if (key != null) Load();
            return;
        }
        Flush();
    }

    /// <summary>Write pending changes now (also called on logic unload).</summary>
    public static void Flush()
    {
        if (!_dirty || _key == null) return;
        _dirty = false;
        try
        {
            var lines = new List<string> { "# Expedition Helper: per-location choices and last teams for one museum" };
            lines.AddRange(RepeatChoices.Save());
            lines.AddRange(LastLoadouts.Save());
            Directory.CreateDirectory(Folder);
            string path = PathFor(_key), temp = path + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) { Entry.Host.Log.LogError($"[museum] saving {_key} failed: {e}"); }
    }

    private static void Load()
    {
        string path = PathFor(_key);
        if (!File.Exists(path)) { Entry.Host.Log.LogInfo($"[museum] {_key}: nothing stored yet"); return; }
        int count = 0;
        try
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (RepeatChoices.Load(parts) || LastLoadouts.Load(parts)) count++;
                else Entry.Host.Log.LogWarning($"[museum] {_key}: ignoring line '{line}'");
            }
            Entry.Host.Log.LogInfo($"[museum] {_key}: loaded {count} entr{(count == 1 ? "y" : "ies")}");
        }
        catch (Exception e) { Entry.Host.Log.LogError($"[museum] loading {_key} failed: {e}"); }
    }

    /// <summary>e.g. "career-1002-12345-20250906T130712", or null when no museum is loaded.</summary>
    public static string CurrentKey()
    {
        var level = TPS.Core.StaticGameSystem<LevelManager>.Instance?.Level;
        var history = level?.LevelPlaythroughHistory;
        if (history == null || level.Config == null || ExpeditionManager.Instance == null) return null;
        string mode = level.IsSandboxLevel ? "sandbox" : "career";
        return $"{mode}-{level.Config.LevelID.ID}-{history.LevelPlaythroughID}-{history.DatePlaythroughStarted.ToString("yyyyMMdd'T'HHmmss")}";
    }

    private static string Folder => Path.Combine(BepInEx.Paths.ConfigPath, "ExpeditionHelper", "museums");

    private static string PathFor(string key) => Path.Combine(Folder, key + ".txt");

    /// <summary>Parse "a,b,c" of longs, skipping anything that isn't one.</summary>
    public static IEnumerable<long> Longs(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => long.TryParse(s, out long v) ? v : 0).Where(v => v != 0);
}
