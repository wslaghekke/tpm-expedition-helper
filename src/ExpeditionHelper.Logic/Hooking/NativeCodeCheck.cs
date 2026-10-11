using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;

namespace ExpeditionHelper.Logic.Hooking;

/// <summary>
/// Checks whether the native code behind an il2cpp method can take an inline detour.
/// In GameAssembly.dll that's often not the case:
///  - the compiler folds identical bodies (every empty method shares one 3-byte `ret` stub), so
///    detouring one "method" detours thousands — this crashed the game once;
///  - small thunks (`mov; jmp next`) are shorter than the detour and would corrupt the next function;
///  - Denuvo relocates some functions into the protected `.sbss` section.
/// </summary>
internal static class NativeCodeCheck
{
    /// <summary>Bytes overwritten by an absolute x64 jump detour (Dobby/Funchook worst case).</summary>
    private const int DetourBytes = 14;

    private static Dictionary<long, int> _pointerUseCount;
    private static (long Base, List<(string Name, long Start, long End)> Sections)? _module;

    /// <summary>The method's current native code address, resolved by name through il2cpp metadata.</summary>
    public static IntPtr NativeCode(MethodBase target)
    {
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(target);
        if (field == null) return IntPtr.Zero;
        var methodInfo = (IntPtr)field.GetValue(null);
        return methodInfo == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(methodInfo); // MethodInfo.methodPointer is the first field
    }

    /// <summary>null when the native code can be detoured; otherwise the reason.</summary>
    public static string WhyUnsafe(MethodBase target)
    {
        IntPtr code = NativeCode(target);
        if (code == IntPtr.Zero) return "no native method pointer";

        var (moduleBase, sections) = Module();
        long addr = (long)code;
        string section = sections.FirstOrDefault(s => addr >= s.Start && addr < s.End).Name;
        if (section != "il2cpp")
            return $"code at rva 0x{addr - moduleBase:X} is in '{section ?? "outside GameAssembly"}', not the normal il2cpp section (Denuvo-protected?)";

        int shared = PointerUseCount().GetValueOrDefault(addr);
        if (shared > 1) return $"native body is shared with {shared - 1} other method(s) (identical code folding)";

        // Decode the instructions the detour will overwrite; any control-flow exit or padding inside
        // that window means the function is shorter than the detour (or something already hooked it).
        var bytes = new byte[32];
        Marshal.Copy(code, bytes, 0, bytes.Length);
        var decoder = Iced.Intel.Decoder.Create(64, new Iced.Intel.ByteArrayCodeReader(bytes));
        decoder.IP = (ulong)addr;
        while (decoder.IP - (ulong)addr < DetourBytes)
        {
            decoder.Decode(out var ins);
            if (ins.IsInvalid) return $"undecodable instruction at +{ins.IP - (ulong)addr} ({BitConverter.ToString(bytes, 0, 16)})";
            if (ins.Mnemonic == Iced.Intel.Mnemonic.Int3 || ins.FlowControl is Iced.Intel.FlowControl.Return
                    or Iced.Intel.FlowControl.UnconditionalBranch or Iced.Intel.FlowControl.IndirectBranch)
                return $"'{ins}' at +{ins.IP - (ulong)addr} is inside the {DetourBytes}-byte detour window (thunk/stub or already hooked)";
        }
        return null;
    }

    private static (long, List<(string Name, long Start, long End)>) Module()
    {
        if (_module is { } m) return m;
        var mod = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .First(x => x.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
        IntPtr b = mod.BaseAddress;
        int pe = Marshal.ReadInt32(b, 0x3C);
        int count = Marshal.ReadInt16(b, pe + 6);
        int optSize = Marshal.ReadInt16(b, pe + 20);
        var sections = new List<(string, long, long)>();
        for (int i = 0; i < count; i++)
        {
            IntPtr h = b + pe + 24 + optSize + i * 40;
            var name = new byte[8];
            Marshal.Copy(h, name, 0, 8);
            long va = (uint)Marshal.ReadInt32(h, 12), size = (uint)Marshal.ReadInt32(h, 8);
            sections.Add((System.Text.Encoding.ASCII.GetString(name).TrimEnd('\0'), (long)b + va, (long)b + va + size));
        }
        _module = ((long)b, sections);
        return _module.Value;
    }

    /// <summary>How many il2cpp methods (across all loaded images) point at each native address.</summary>
    private static unsafe Dictionary<long, int> PointerUseCount()
    {
        if (_pointerUseCount != null) return _pointerUseCount;
        var sw = Stopwatch.StartNew();
        var counts = new Dictionary<long, int>();
        IntPtr domain = IL2CPP.il2cpp_domain_get();
        uint asmCount = 0;
        IntPtr* assemblies = IL2CPP.il2cpp_domain_get_assemblies(domain, ref asmCount);
        for (int a = 0; a < asmCount; a++)
        {
            IntPtr image = IL2CPP.il2cpp_assembly_get_image(assemblies[a]);
            uint classCount = IL2CPP.il2cpp_image_get_class_count(image);
            for (uint c = 0; c < classCount; c++)
            {
                IntPtr klass = IL2CPP.il2cpp_image_get_class(image, c);
                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    long ptr = (long)Marshal.ReadIntPtr(method);
                    if (ptr == 0) continue;
                    counts[ptr] = counts.GetValueOrDefault(ptr) + 1;
                }
            }
        }
        _pointerUseCount = counts;
        Entry.Host.Log.LogInfo($"[hook] indexed {counts.Values.Sum()} il2cpp methods / {counts.Count} native bodies in {sw.ElapsedMilliseconds} ms");
        return counts;
    }
}
