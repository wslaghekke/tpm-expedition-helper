using System;
using System.Runtime.InteropServices;
using ExpeditionHelper.Logic.Hooking;
using TPS.Game;

namespace ExpeditionHelper.Logic.Features;

/// <summary>
/// Removes the half-second blackout when a repeat starts an expedition.
///
/// WorldMapScene stays loaded (and subscribed) after the map is closed, and its
/// OnExpeditionStarted handler always runs the map's return-to-level transition (fade to black,
/// re-show the level, fade back in) — it only checks that no such transition is already running.
/// From the map that's the exit animation; for an expedition started while the player is already
/// in the museum (our repeats, via ExpeditionManager.AddExpedition) it's a pointless blackout.
/// Skip it while the map is closed: everything the transition restores (music, radio, ECS
/// systems, level visibility) is already in that state then.
/// </summary>
internal static class SkipClosedMapFade
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnExpeditionStartedFn(IntPtr self, IntPtr evt, IntPtr method);

    private static NativeHook<OnExpeditionStartedFn> _hook;

    public static void Install()
    {
        _hook = NativeHook<OnExpeditionStartedFn>.TryCreate(
            typeof(WorldMapScene).GetMethod(nameof(WorldMapScene.OnExpeditionStarted)), OnExpeditionStarted);
    }

    private static void OnExpeditionStarted(IntPtr self, IntPtr evt, IntPtr method)
    {
        try
        {
            if (!new WorldMapScene(self).IsOpen) return;
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[skip-fade] {e}");
        }
        _hook.Original(self, evt, method);
    }
}
