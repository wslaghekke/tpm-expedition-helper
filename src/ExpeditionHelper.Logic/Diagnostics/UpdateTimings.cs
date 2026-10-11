using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ExpeditionHelper.Logic.Diagnostics;

/// <summary>
/// Measures the time each per-frame handler costs during normal play: total, call count and the
/// slowest single call (a once-a-second check that's slow shows up as a spike, not in the average).
/// Two timestamp reads per handler per frame; cheap enough to leave on.
/// Read with the dev bridge: Host.Exports["Perf.Report"], reset with Host.Exports["Perf.Reset"].
/// </summary>
internal static class UpdateTimings
{
    private sealed class Stat
    {
        public long Calls, Ticks, MaxTicks;
    }

    private static readonly Dictionary<string, Stat> Stats = new();
    private static int _startFrame = -1;
    private static long _startTimestamp;

    public static Action Wrap(string name, Action handler)
    {
        var stat = Stats[name] = new Stat();
        return () =>
        {
            long t0 = Stopwatch.GetTimestamp();
            try { handler(); }
            finally
            {
                long d = Stopwatch.GetTimestamp() - t0;
                stat.Calls++;
                stat.Ticks += d;
                if (d > stat.MaxTicks) stat.MaxTicks = d;
            }
        };
    }

    public static void Reset()
    {
        foreach (var s in Stats.Values) s.Calls = s.Ticks = s.MaxTicks = 0;
        _startFrame = UnityEngine.Time.frameCount;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public static string Report()
    {
        if (_startFrame < 0) Reset();
        int frames = UnityEngine.Time.frameCount - _startFrame;
        double seconds = (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency;
        double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        var lines = new List<string>
        {
            $"{frames} frames in {seconds:F0} s ({(seconds > 0 ? frames / seconds : 0):F0} fps, {(frames > 0 ? seconds * 1000 / frames : 0):F2} ms/frame)",
        };
        long totalTicks = 0;
        foreach (var (name, s) in Stats.OrderByDescending(kv => kv.Value.Ticks))
        {
            totalTicks += s.Ticks;
            lines.Add($"  {name,-12} {Ms(s.Ticks) / Math.Max(frames, 1) * 1000:F2} µs/frame avg, slowest call {Ms(s.MaxTicks):F3} ms, {s.Calls} calls");
        }
        if (frames > 0)
            lines.Add($"  total        {Ms(totalTicks) / frames * 1000:F2} µs/frame = {Ms(totalTicks) / (seconds * 1000) * 100:F3}% of frame time");
        return string.Join("\n", lines);
    }
}
