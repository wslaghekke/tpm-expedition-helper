using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using ExpeditionHelper.Host;
using ExpeditionHelper.Logic.Hooking;
using TPS.Core.Localisation;
using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// An in-game message popup owned by the mod: custom title/text (updatable while open) and up to
/// two buttons, shown through the game's own MessageQueuePopup.SendMessage.
///
/// Button clicks normally go to a managed callback (Func&lt;int,bool&gt;), which we can't create
/// (Denuvo breaks delegate conversion). Instead we send with a null callback and detour
/// MessagePopup.OnChoice(int) — a unique virtual body every popup button goes through — and
/// handle clicks on our own popups there without calling the original (which would invoke the
/// null callback).
/// </summary>
internal sealed class GamePopup
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool OnChoiceFn(IntPtr self, int choice, IntPtr method);

    private static NativeHook<OnChoiceFn> _onChoiceHook;
    private static readonly Dictionary<IntPtr, GamePopup> Open = new();

    private readonly MessagePopup _popup;

    /// <summary>Raised on the main thread with the clicked button index (0 = first button).</summary>
    public event Action<int> Clicked;

    public bool IsOpen => Open.ContainsKey(_popup.Pointer);

    private GamePopup(MessagePopup popup) => _popup = popup;

    /// <summary>Show a popup; returns null if the game's message queue isn't available.</summary>
    public static GamePopup Show(string title, string text, string button1 = null, string button2 = null, bool pauseGame = false)
    {
        if (!EnsureHook()) return null;
        var queue = GameApp.Instance?.MessageQueue;
        if (queue == null) return null;

        var message = queue.SendMessage(title, text, Label(button1), Label(button2),
            defaultChoice: 0, response: null, pauseOverride: pauseGame, showImmediately: true, persist: true, showOverFullscreenMenus: false);
        var popup = message?.TryCast<MessagePopup>();
        if (popup == null) return null;

        var result = new GamePopup(popup);
        Open[popup.Pointer] = result;
        return result;
    }

    public void SetText(string text)
    {
        if (!IsOpen) return;
        _popup.Text = text;
        _popup.UIInstance?.SetText(text); // the UI copies the text when it's set up
    }

    public void Close()
    {
        if (!Open.Remove(_popup.Pointer)) return;
        GameApp.Instance?.MessageQueue?.Close(_popup);
    }

    /// <summary>Close every popup this logic version opened (called on unload).</summary>
    public static void CloseAll()
    {
        foreach (var p in new List<GamePopup>(Open.Values)) p.Close();
        Open.Clear();
        _onChoiceHook = null; // disposed by Hooks.DisposeAll
    }

    // A LocalisedString with only dev text renders that text as-is (Translation falls back to _dev).
    private static LocalisedString Label(string text)
    {
        var ls = new LocalisedString();
        if (!string.IsNullOrEmpty(text)) ls._dev = text;
        return ls;
    }

    private static bool EnsureHook()
    {
        if (_onChoiceHook != null) return true;
        var target = typeof(MessagePopup).GetMethod(nameof(MessagePopup.OnChoice), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        _onChoiceHook = NativeHook<OnChoiceFn>.TryCreate(target, OnChoiceDetour);
        return _onChoiceHook != null;
    }

    private static bool OnChoiceDetour(IntPtr self, int choice, IntPtr method)
    {
        try
        {
            if (!Open.TryGetValue(self, out var popup))
                return _onChoiceHook.Original(self, choice, method);

            // Ours: handle it here and close it ourselves after the click has finished processing.
            Open.Remove(self);
            MainThread.Post(() =>
            {
                GameApp.Instance?.MessageQueue?.Close(popup._popup);
                popup.Clicked?.Invoke(choice);
            });
            return true;
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[popup] OnChoice detour failed: {e}");
            return true;
        }
    }
}
