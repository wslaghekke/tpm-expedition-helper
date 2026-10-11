using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ExpeditionHelper.Host;
using ExpeditionHelper.Logic.Hooking;
using TPS.Core.Localisation;
using TPS.Game;
using TPS.Game.UI;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// A non-blocking advisor notification (the kind the game uses for "Expedition Complete") owned
/// by the mod: custom title/text that can be updated while queued or shown, and an optional
/// action button (e.g. "Cancel") next to Dismiss.
///
/// The definition is built by copying every field from the game's ExpeditionComplete definition
/// (a default-constructed definition has null localisation/visual fields and crashes the game),
/// then overriding title, text and button.
///
/// Clicks: AdvisorUI wires its two buttons to the lambdas _Setup_b__19_0/_1, which act on
/// AdvisorUI._message. We detour both; for our notices we handle the click ourselves (the game's
/// path would invoke the null response callback — we can't create managed callbacks under Denuvo).
/// </summary>
internal sealed class AdvisorNotice
{
    public enum Button { Lambda0, Lambda1 }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ButtonLambdaFn(IntPtr self, IntPtr method);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnOpenMessageFn(IntPtr self, IntPtr message, IntPtr method);

    private static NativeHook<ButtonLambdaFn> _hook0, _hook1;
    private static NativeHook<OnOpenMessageFn> _openHook;
    private static readonly Dictionary<IntPtr, AdvisorNotice> Live = new();

    private readonly MessageAdvisor _message;
    private readonly string _actionLabel;

    /// <summary>Raised on the main thread when one of the notice's buttons is clicked.</summary>
    public event Action<Button> Clicked;

    private AdvisorNotice(MessageAdvisor message, string actionLabel)
    {
        _message = message;
        _actionLabel = actionLabel;
    }

    public static AdvisorNotice Show(string title, string text, string actionLabel = null, EAdvisorMessageStyle style = EAdvisorMessageStyle.Notification, float displayTime = 10f)
    {
        if (!EnsureHooks()) return null;
        var template = GameApp.Instance?.MessageQueue?.Config?.ExpeditionComplete;
        if (template == null) return null;

        var def = new MessageAdvisorDefinition
        {
            Title = Label(title),
            Text = Label(text),
            Icon = template.Icon,
            MessageStyle = style,
            HideDismissButton = false,
            ShowActionButton = actionLabel != null,
            ActionString = Label(actionLabel),
            DisplayTime = displayTime, // seconds before it closes by itself; 0 = stays until acted on
            ShowWhenGamePaused = template.ShowWhenGamePaused,
            CharacterImage = template.CharacterImage,
            CharacterModule = template.CharacterModule,
            CharacterPrefab = template.CharacterPrefab,
            AdvisorAnimation = template.AdvisorAnimation,
        };
        var message = def.CreateMessage(null)?.TryCast<MessageAdvisor>();
        if (message == null) return null;
        message.Title = title;
        message.Text = text;
        message.Send();

        var notice = new AdvisorNotice(message, actionLabel);
        Live[message.Pointer] = notice;
        return notice;
    }

    public bool IsLive => Live.ContainsKey(_message.Pointer);

    public void SetText(string text)
    {
        if (!IsLive) return;
        _message.Text = text;
        var ui = CurrentUi();
        if (ui != null && ui._message?.Pointer == _message.Pointer && ui._text != null) ui._text.text = text;
    }

    public void Close()
    {
        if (!Live.Remove(_message.Pointer)) return;
        var ui = CurrentUi();
        if (ui != null && ui._message?.Pointer == _message.Pointer) ui.CloseCurrentMessage();
        else _message.Dismiss();
    }

    public static void CloseAll()
    {
        foreach (var n in new List<AdvisorNotice>(Live.Values)) n.Close();
        Live.Clear();
        _hook0 = _hook1 = null; // disposed by Hooks.DisposeAll
        _openHook = null;
    }

    private static AdvisorUI _ui;
    private static AdvisorUI CurrentUi() => _ui ??= UnityEngine.Object.FindObjectOfType<AdvisorUI>();

    private static LocalisedString Label(string text)
    {
        var ls = new LocalisedString();
        if (!string.IsNullOrEmpty(text)) ls._dev = text;
        return ls;
    }

    private static bool EnsureHooks()
    {
        if (_hook0 != null && _hook1 != null && _openHook != null) return true;
        _openHook ??= NativeHook<OnOpenMessageFn>.TryCreate(typeof(AdvisorUI).GetMethod(nameof(AdvisorUI.OnOpenMessage)), OnOpenMessage);
        _hook0 ??= NativeHook<ButtonLambdaFn>.TryCreate(typeof(AdvisorUI).GetMethod("_Setup_b__19_0"), (s, m) => OnButton(s, m, Button.Lambda0));
        _hook1 ??= NativeHook<ButtonLambdaFn>.TryCreate(typeof(AdvisorUI).GetMethod("_Setup_b__19_1"), (s, m) => OnButton(s, m, Button.Lambda1));
        return _hook0 != null && _hook1 != null && _openHook != null;
    }

    /// <summary>
    /// The action button's label is a Localize component that resolves a translation term; our
    /// plain-text label has no term, so it would keep the previous message's text. Set it directly
    /// while our notice is shown, and hand control back to Localize for the game's own messages.
    /// </summary>
    private static void OnOpenMessage(IntPtr self, IntPtr message, IntPtr method)
    {
        _openHook.Original(self, message, method);
        try
        {
            var ui = new AdvisorUI(self);
            _ui = ui;
            var localize = ui._actionButtonText;
            if (localize == null) return;
            bool ours = Live.TryGetValue(message, out var notice);
            localize.enabled = !ours;
            if (ours && notice._actionLabel != null)
            {
                var label = localize.GetComponent<TMPro.TMP_Text>();
                if (label != null) label.text = notice._actionLabel;
            }
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[notice] OnOpenMessage detour failed: {e}");
        }
    }

    private static void OnButton(IntPtr self, IntPtr method, Button which)
    {
        try
        {
            var ui = new AdvisorUI(self);
            _ui = ui;
            var current = ui._message;
            if (current == null || !Live.TryGetValue(current.Pointer, out var notice))
            {
                (which == Button.Lambda0 ? _hook0 : _hook1).Original(self, method);
                return;
            }

            Live.Remove(current.Pointer);
            ui.CloseCurrentMessage();
            MainThread.Post(() => notice.Clicked?.Invoke(which));
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[notice] button detour failed: {e}");
        }
    }
}
