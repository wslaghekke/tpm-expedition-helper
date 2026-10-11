using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace ExpeditionHelper.Host.Dev;

/// <summary>Globals visible to /eval scripts.</summary>
public sealed class ScriptGlobals
{
    internal readonly StringBuilder Output = new();

    public HostApi Host { get; init; } = null!;

    /// <summary>The currently loaded logic assembly (types reachable via reflection).</summary>
    public Assembly Logic { get; init; }

    /// <summary>Append a line to the response's "output".</summary>
    public void Print(object value) => Output.AppendLine(ScriptEval.Describe(value, 1));

    /// <summary>Describe an object's members (reflection over the interop wrapper).</summary>
    public string Dump(object value, int depth = 1) => ScriptEval.Describe(value, depth);
}

/// <summary>
/// Compiles C# snippets with Roslyn against every loaded assembly plus the game interop
/// assemblies, then runs them on the Unity main thread.
/// </summary>
internal static class ScriptEval
{
    private static ScriptOptions? _options;
    private static readonly object OptionsLock = new();

    private static readonly string[] CandidateImports =
    {
        "UnityEngine", "TPS.Game", "TPS.Game.UI", "TPS.Core",
        "Il2CppInterop.Runtime", "Il2CppInterop.Runtime.InteropTypes.Arrays",
    };

    public static async Task<(int, string)> Eval(string code, HostApi api, LogicLoader loader)
    {
        if (string.IsNullOrWhiteSpace(code))
            return (400, Json.Obj(("ok", false), ("error", "empty body; POST C# source")));

        var options = await Task.Run(GetOptions);
        var script = CSharpScript.Create<object>(code, options, typeof(ScriptGlobals));

        // Compile off the main thread so the game doesn't hitch for the compile itself.
        var diagnostics = await Task.Run(() => script.Compile());
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
        if (errors.Length > 0)
            return (400, Json.Obj(("ok", false), ("stage", "compile"), ("errors", errors)));

        var globals = new ScriptGlobals { Host = api, Logic = loader.Current };
        try
        {
            // The synchronous part of the script runs on the main thread; anything after an
            // `await` in the script continues on the thread pool.
            var state = await await MainThread.Run(() => script.RunAsync(globals));
            object value = state.ReturnValue;
            string described = await MainThread.Run(() => Describe(value, 1));
            return (200, Json.Obj(
                ("ok", true),
                ("type", value?.GetType().FullName),
                ("result", described),
                ("output", globals.Output.ToString())));
        }
        catch (Exception e)
        {
            return (500, Json.Obj(("ok", false), ("stage", "run"), ("error", e.ToString()), ("output", globals.Output.ToString())));
        }
    }

    private static ScriptOptions GetOptions()
    {
        lock (OptionsLock)
        {
            if (_options != null) return _options;

            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
                byName.TryAdd(asm.GetName().Name!, asm.Location);
            }
            string interopDir = Path.Combine(Paths.BepInExRootPath, "interop");
            if (Directory.Exists(interopDir))
                foreach (var dll in Directory.EnumerateFiles(interopDir, "*.dll"))
                    byName.TryAdd(Path.GetFileNameWithoutExtension(dll), dll);
            var refs = byName.Values.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();

            var namespaces = CollectNamespaces(byName.Values.Where(p => p.StartsWith(interopDir, StringComparison.OrdinalIgnoreCase)));
            var imports = new List<string> { "System", "System.Linq", "System.Collections.Generic", "System.Reflection" };
            imports.AddRange(CandidateImports.Where(namespaces.Contains));

            _options = ScriptOptions.Default
                .WithReferences(refs)
                .WithImports(imports)
                .WithAllowUnsafe(true)
                .WithEmitDebugInformation(false);
            return _options;
        }
    }

    private static HashSet<string> CollectNamespaces(IEnumerable<string> dlls)
    {
        var set = new HashSet<string>();
        foreach (var path in dlls)
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var pe = new PEReader(fs);
                var md = pe.GetMetadataReader();
                foreach (var h in md.TypeDefinitions)
                    set.Add(md.GetString(md.GetTypeDefinition(h).Namespace));
            }
            catch { /* not a managed assembly */ }
        }
        return set;
    }

    private static readonly HashSet<string> SkipMembers = new()
    {
        "Pointer", "ObjectClass", "WasCollected", "isWrapped", "pooledPtr", "hideFlags",
    };

    internal static string Describe(object value, int depth)
    {
        var sb = new StringBuilder();
        Describe(sb, value, depth, "");
        return sb.ToString();
    }

    private static void Describe(StringBuilder sb, object value, int depth, string indent)
    {
        if (value == null) { sb.Append("null"); return; }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || value is decimal)
        {
            sb.Append(value);
            return;
        }

        string str = SafeToString(value);
        sb.Append($"[{type.FullName}] {str}");
        if (depth <= 0) return;

        if (value is IEnumerable e)
        {
            int i = 0;
            foreach (var item in e)
            {
                sb.Append('\n').Append(indent).Append($"  [{i}] ");
                Describe(sb, item, depth - 1, indent + "  ");
                if (++i >= 50) { sb.Append('\n').Append(indent).Append("  ..."); break; }
            }
            return;
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || SkipMembers.Contains(p.Name)) continue;
            if (p.DeclaringType?.Namespace?.StartsWith("Il2CppInterop") == true) continue;
            sb.Append('\n').Append(indent).Append("  ").Append(p.Name).Append(" = ");
            try { Describe(sb, p.GetValue(value), depth - 1, indent + "  "); }
            catch (Exception ex) { sb.Append($"<{(ex.InnerException ?? ex).GetType().Name}>"); }
        }
    }

    private static string SafeToString(object value)
    {
        try
        {
            string s = value.ToString() ?? "";
            return s.Length > 300 ? s[..300] + "…" : s;
        }
        catch (Exception ex) { return $"<ToString threw {ex.GetType().Name}>"; }
    }
}
