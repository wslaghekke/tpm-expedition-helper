using System;
using ExpeditionHelper.Host;
using ExpeditionHelper.Logic.Hooking;

namespace ExpeditionHelper.Logic;

/// <summary>
/// Called by the host's LogicLoader. Load runs on every (re)load, Unload before the next one.
/// Rules for code in this assembly:
///  - never call ClassInjector or convert delegates to IL2CPP (Action -> UnityAction etc.):
///    Denuvo breaks Il2CppInterop's injection on this game;
///  - hook game methods only through <see cref="NativeHook{TDelegate}"/> (not Harmony), which
///    registers the hook so Unload removes it;
///  - subscribe to per-frame/config events through <see cref="HostApi"/>, which clears them on reload;
///  - destroy any GameObjects/UI you create in Unload.
/// </summary>
public static class Entry
{
    internal static HostApi Host { get; private set; } = null!;
    internal static Settings Settings { get; private set; } = null!;
    internal static Workshop.WorkshopSettings WorkshopSettings { get; private set; } = null!;

    public static void Load(HostApi host)
    {
        Host = host;
        Settings = new Settings(host.Config);
        WorkshopSettings = new Workshop.WorkshopSettings(host.Config);

        host.ConfigChanged += e =>
        {
            host.Log.LogInfo($"Setting changed: {e.ChangedSetting.Definition} = {e.ChangedSetting.BoxedValue}");
            MainThread.Post(Game.SettingsMenuTab.Refresh); // .cfg reloads arrive on the file watcher's thread
            if (e.ChangedSetting == WorkshopSettings.WiderProjectMenu) MainThread.Post(Workshop.WorkshopLayout.Refresh);
        };

        // Dev bridge access (tools/snippets).
        host.Exports["NativeCodeCheck.WhyUnsafe"] = (Func<System.Reflection.MethodBase, string>)NativeCodeCheck.WhyUnsafe;
        host.Exports["Expeditions.ClaimFirstToInventory"] = (Func<string>)(() =>
            Game.ExpeditionActions.Claim(Game.ExpeditionActions.FirstClaimable(), moveToInventory: true));

        host.Exports["Popup.Test"] = (Func<string>)(() =>
        {
            var p = Game.GamePopup.Show("Expedition Helper", "Test: repeat for Digiverse Dredge is waiting for staff.\nClick Cancel to test the button.", "Cancel");
            if (p == null) return "popup not shown (see log)";
            p.Clicked += choice => host.Log.LogInfo($"[popup] test popup clicked: button {choice}");
            host.Exports["Popup.Last"] = p;
            return "shown";
        });

        host.Exports["Notice.Test"] = (Func<string>)(() =>
        {
            var n = Game.AdvisorNotice.Show("Expedition Helper", "Test: repeat for Digiverse Dredge is waiting for staff.", "Cancel");
            if (n == null) return "notice not shown (see log)";
            n.Clicked += b => host.Log.LogInfo($"[notice] test notice clicked: {b}");
            host.Exports["Notice.Last"] = n;
            return "shown";
        });

        host.Exports["Inbox.Test"] = (Func<string>)(() =>
        {
            var n = Game.InboxNotice.Send("Expedition Helper", "Test: repeat for Two Point Sea – Pebberley Reef is waiting.\nWaiting for: Kate Chiptune to recover (Banged Up).", "Cancel repeat");
            if (n == null) return "inbox notice not sent (see log)";
            n.Clicked += () => host.Log.LogInfo("[inbox] test notice clicked");
            host.Exports["Inbox.Last"] = n;
            return "sent";
        });

        Game.InboxNotice.RemoveAll(); // orphans from a previous logic version

        host.Update += Diagnostics.UpdateTimings.Wrap("museum-data", Features.MuseumData.Update); // first: the others read it

        var autoClaim = new Features.AutoClaim(Settings);
        host.Update += Diagnostics.UpdateTimings.Wrap("auto-claim", autoClaim.Update);

        var repeat = new Features.RepeatExpeditions(Settings);
        host.Update += Diagnostics.UpdateTimings.Wrap("repeat", repeat.Update);
        host.Exports["Repeat.DryRun"] = (Func<string>)repeat.DryRun;
        host.Exports["Repeat.StartNow"] = (Func<string, string>)repeat.StartNow;
        Features.SkipClosedMapFade.Install();

        Features.SettingsTab.Install(Settings, WorkshopSettings);
        Workshop.WorkshopLayout.Install(WorkshopSettings);
        var setupOption = new Features.SetupRepeatOption(Settings);
        host.Update += Diagnostics.UpdateTimings.Wrap("setup-screen", setupOption.Update);
        var lastTeam = new Features.SetupLastTeam();
        host.Update += Diagnostics.UpdateTimings.Wrap("last-team", lastTeam.Update);
        host.Exports["Perf.Report"] = (Func<string>)Diagnostics.UpdateTimings.Report;
        host.Exports["Perf.Reset"] = (Action)Diagnostics.UpdateTimings.Reset;
        Diagnostics.UpdateTimings.Reset();

        host.Log.LogInfo($"Logic loaded (built {BuildStamp()})");
    }

    public static void Unload()
    {
        Features.MuseumData.Flush();
        Game.GamePopup.CloseAll();
        Game.AdvisorNotice.CloseAll();
        Game.InboxNotice.RemoveAll();
        Game.SettingsMenuTab.Uninstall();
        Game.ExpeditionSetupSection.Uninstall();
        Game.UiClicks.Reset();
        Game.UiDropdowns.Reset();
        Game.ExpeditionActions.Reset();
        Workshop.WorkshopLayout.Uninstall();
        Hooks.DisposeAll();
        Host?.Log.LogInfo("Logic unloaded");
    }

    private static string BuildStamp()
    {
        var path = typeof(Entry).Assembly.Location;
        return string.IsNullOrEmpty(path) ? DateTime.Now.ToString("HH:mm:ss") : System.IO.File.GetLastWriteTime(path).ToString("HH:mm:ss");
    }
}
