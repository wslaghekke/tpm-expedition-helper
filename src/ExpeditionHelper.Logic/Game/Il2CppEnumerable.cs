using System.Collections.Generic;

namespace ExpeditionHelper.Logic.Game;

internal static class Il2CppEnumerable
{
    /// <summary>
    /// Iterate an Il2Cpp IEnumerable&lt;T&gt; (e.g. a game method returning IEnumerable). The interop
    /// IEnumerator&lt;T&gt; wrapper has no MoveNext, so step through the non-generic interface.
    /// Only use this for value types or concrete game classes: IEnumerable&lt;object&gt; (boxed keys)
    /// returned garbage pointers and crashed the game.
    /// </summary>
    public static IEnumerable<T> Iter<T>(this Il2CppSystem.Collections.Generic.IEnumerable<T> source)
    {
        if (source == null) yield break;
        var enumerator = source.GetEnumerator();
        var nonGeneric = enumerator.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (nonGeneric.MoveNext()) yield return enumerator.Current;
    }
}
