using System;
using HarmonyLib;

namespace ExpeditionHelper.Host;

/// <summary>
/// Per-frame tick without injecting an IL2CPP type.
///
/// The Windows build is protected by Denuvo Anti-Tamper, which obfuscates the il2cpp runtime
/// internals that Il2CppInterop's ClassInjector must hook. So we can't register our own
/// MonoBehaviour (or convert delegates); instead we postfix the game's central system update,
/// which runs every frame on the main thread, in menus and in-level.
/// </summary>
internal static class FrameTick
{
    public const string HarmonyId = "tpm.expeditionhelper.host";
    private static int _lastFrame = -1;

    public static void Install()
    {
        var harmony = new Harmony(HarmonyId);
        harmony.Patch(
            AccessTools.Method(typeof(TPS.Core.GameSystems), nameof(TPS.Core.GameSystems.Update)),
            postfix: new HarmonyMethod(typeof(FrameTick), nameof(Postfix)));
    }

    private static void Postfix()
    {
        // There may be more than one GameSystems instance (global + level); tick once per frame.
        int frame = UnityEngine.Time.frameCount;
        if (frame == _lastFrame) return;
        _lastFrame = frame;

        try
        {
            MainThread.Drain();
            Plugin.Instance.Api.RaiseUpdate();
        }
        catch (Exception e)
        {
            // Never let our code throw into the game's update loop.
            Plugin.Logger.LogError($"Frame tick failed: {e}");
        }
    }
}
