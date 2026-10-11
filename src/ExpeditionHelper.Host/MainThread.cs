using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ExpeditionHelper.Host;

/// <summary>Unity objects may only be touched from the main thread; marshal work onto it.</summary>
public static class MainThread
{
    private static readonly ConcurrentQueue<Action> Queue = new();

    public static void Post(Action action) => Queue.Enqueue(action);

    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Enqueue(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }

    internal static void Drain()
    {
        while (Queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Plugin.Logger.LogError($"Main-thread task failed: {e}"); }
        }
    }
}
