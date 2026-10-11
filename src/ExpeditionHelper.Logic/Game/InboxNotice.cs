using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ExpeditionHelper.Host;
using ExpeditionHelper.Logic.Hooking;
using TPS.Core.Localisation;
using TPS.Game;
using TPS.Game.UI;
using UnityEngine.AddressableAssets;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// A persistent message in the game's inbox (the envelope tray), owned by the mod: custom title
/// and text (updatable while it's in the inbox), one button whose label we choose, removable.
///
/// Uses the game's own "Generic" inbox type (InboxMessageType_Generic: shown as "Notification",
/// opens the generic title+description view). That asset isn't referenced by any loaded config,
/// so it's loaded by its Addressables GUID (found in the content catalog). The definition copies
/// its remaining fields from a real inbox definition — a default-constructed definition with
/// empty fields crashed the game.
///
/// The generic view doesn't show Message.Text (it shows placeholder text), and its button would
/// invoke the null response callback, so we detour its Open (fill in text + button label) and
/// InboxMessage.OnChoice (handle clicks on our messages ourselves).
/// </summary>
internal sealed class InboxNotice
{
    private const string GenericInboxTypeGuid = "4a33c6568d26e604585b403c47351f28"; // InboxMessageType_Generic

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OpenFn(IntPtr self, IntPtr message, IntPtr method);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool OnChoiceFn(IntPtr self, int choice, IntPtr method);

    private static NativeHook<OpenFn> _openHook;
    private static NativeHook<OnChoiceFn> _choiceHook;
    private static InboxMessageType _genericType;
    private static readonly Dictionary<IntPtr, InboxNotice> Live = new();

    private readonly InboxMessage _message;
    private readonly string _buttonLabel;
    private string _text;
    private InboxMenu_Message_Generic _openView;

    /// <summary>Raised on the main thread when the player clicks the message's button.</summary>
    public event Action Clicked;

    private InboxNotice(InboxMessage message, string text, string buttonLabel)
    {
        _message = message;
        _text = text;
        _buttonLabel = buttonLabel;
    }

    public bool IsLive => Live.ContainsKey(_message.Pointer);

    /// <param name="buttonLabel">Label for the message's single button; null keeps the game's "Ok".</param>
    public static InboxNotice Send(string title, string text, string buttonLabel = null)
    {
        if (!EnsureHooks()) return null;
        var template = GameApp.Instance?.MessageQueue?.Config?.InboxMIA;
        var type = GenericType();
        if (template == null || type == null) return null;

        var def = new InboxMessageDefinition
        {
            Title = Label(title),
            Text = Label(text),
            Icon = template.Icon,
            MessageType = type,
            UseScaledTime = template.UseScaledTime,
            TimeoutInSeconds = 0, // stays until read/removed
        };
        // The base definition's CreateMessage returns null (only the specialised definitions create
        // messages) and InboxMessage_Generic has no constructor in the interop, so build the base
        // message directly; the generic view (chosen by MessageType) accepts any InboxMessage.
        var message = new InboxMessage(def, null);
        message.Title = title;
        message.Text = text;
        message.Send();

        var notice = new InboxNotice(message, text, buttonLabel);
        Live[message.Pointer] = notice;
        return notice;
    }

    public void SetText(string text)
    {
        if (!IsLive || text == _text) return;
        _text = text;
        _message.Text = text;
        if (_openView != null && _openView.isOpen && _openView._textDescription != null)
            _openView._textDescription.text = text;
    }

    public void Remove()
    {
        if (!Live.Remove(_message.Pointer)) return;
        Inbox()?.Pop(_message);
    }

    /// <summary>
    /// Remove every mod message from the inbox, including ones a previous (hot-reloaded) logic
    /// version sent and no longer tracks: anything of the Generic inbox type is ours — the game
    /// itself doesn't use it (the asset isn't even loaded until we load it).
    /// </summary>
    public static void RemoveAll()
    {
        Live.Clear();
        var inbox = Inbox();
        var type = GenericType();
        if (inbox != null && type != null)
        {
            var ours = new List<InboxMessage>();
            foreach (var m in inbox.Messages)
                if (m.Definition?.MessageType?.Pointer == type.Pointer) ours.Add(m);
            foreach (var m in ours) inbox.Pop(m);
        }
        _openHook = null;   // disposed by Hooks.DisposeAll
        _choiceHook = null;
    }

    private static bool EnsureHooks()
    {
        if (_openHook != null && _choiceHook != null) return true;
        _openHook ??= NativeHook<OpenFn>.TryCreate(
            typeof(InboxMenu_Message_Generic).GetMethod(nameof(InboxMenu_Message_Generic.Open)), OnOpen);
        _choiceHook ??= NativeHook<OnChoiceFn>.TryCreate(
            typeof(InboxMessage).GetMethod(nameof(InboxMessage.OnChoice)), OnChoice);
        return _openHook != null && _choiceHook != null;
    }

    private static void OnOpen(IntPtr self, IntPtr message, IntPtr method)
    {
        _openHook.Original(self, message, method);
        try
        {
            if (!Live.TryGetValue(message, out var notice)) return;
            var view = new InboxMenu_Message_Generic(self);
            notice._openView = view;
            if (view._textDescription != null) view._textDescription.text = notice._text;
            if (notice._buttonLabel != null && view._buttons != null && view._buttons.Length > 0)
            {
                var label = view._buttons[0]?.GetComponentInChildren<TMPro.TMP_Text>();
                if (label != null) label.text = notice._buttonLabel;
            }
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[inbox] Open detour failed: {e}");
        }
    }

    private static bool OnChoice(IntPtr self, int choice, IntPtr method)
    {
        try
        {
            if (!Live.TryGetValue(self, out var notice))
                return _choiceHook.Original(self, choice, method);

            // Ours: remove it from the inbox ourselves (the game's path would invoke the null
            // response callback) and report the click after the UI has finished handling it.
            MainThread.Post(() =>
            {
                notice.Remove();
                notice.Clicked?.Invoke();
            });
            return true;
        }
        catch (Exception e)
        {
            Entry.Host.Log.LogError($"[inbox] OnChoice detour failed: {e}");
            return true;
        }
    }

    // FindObjectOfType skips inactive objects, and the tray is hidden while the inbox menu is open.
    private static InboxMessages Inbox()
    {
        foreach (var tray in UnityEngine.Resources.FindObjectsOfTypeAll<InboxTrayUI>())
            if (tray._inbox != null) return tray._inbox;
        return null;
    }

    private static InboxMessageType GenericType()
    {
        if (_genericType != null) return _genericType;
        try { _genericType = Addressables.LoadAssetAsync<InboxMessageType>(GenericInboxTypeGuid).WaitForCompletion(); }
        catch (Exception e) { Entry.Host.Log.LogError($"[inbox] loading the generic inbox type failed: {e.Message}"); }
        return _genericType;
    }

    private static LocalisedString Label(string text)
    {
        var ls = new LocalisedString();
        if (!string.IsNullOrEmpty(text)) ls._dev = text;
        return ls;
    }
}
