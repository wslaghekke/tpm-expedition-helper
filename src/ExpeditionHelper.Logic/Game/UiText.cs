using TPS.Core.Localisation;

namespace ExpeditionHelper.Logic.Game;

/// <summary>Putting our own (untranslated) text into the game's UI.</summary>
internal static class UiText
{
    /// <summary>
    /// Set a label's text and stop its I2 Localize component from replacing it with the
    /// translation of the prefab's original term.
    /// </summary>
    public static void Set(TMPro.TMP_Text text, string value)
    {
        foreach (var localize in text.GetComponents<I2.Loc.Localize>()) localize.enabled = false;
        text.text = value;
    }

    /// <summary>A LocalisedString that shows <paramref name="text"/> as-is (via its dev fallback).</summary>
    public static LocalisedString Plain(string text)
    {
        var ls = new LocalisedString();
        if (!string.IsNullOrEmpty(text)) ls._dev = text;
        return ls;
    }
}
