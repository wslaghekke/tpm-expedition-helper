// Classify the native code behind interop methods: section + first bytes.
// Used to tell Denuvo-mangled functions (unsafe to Harmony-patch) from normal ones.
using System.Runtime.InteropServices;
using HarmonyLib;

var mod = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>()
    .First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
long baseAddr = (long)mod.BaseAddress;

// Parse section headers from the in-memory PE header.
int peOff = Marshal.ReadInt32(mod.BaseAddress, 0x3C);
int numSections = Marshal.ReadInt16(mod.BaseAddress, peOff + 6);
int optSize = Marshal.ReadInt16(mod.BaseAddress, peOff + 20);
var secs = new List<(string name, long start, long end)>();
for (int i = 0; i < numSections; i++)
{
    IntPtr h = mod.BaseAddress + peOff + 24 + optSize + i * 40;
    var nameBytes = new byte[8]; Marshal.Copy(h, nameBytes, 0, 8);
    string name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
    long va = Marshal.ReadInt32(h, 12), vs = Marshal.ReadInt32(h, 8);
    secs.Add((name, baseAddr + va, baseAddr + va + vs));
}
string SectionOf(long a) => secs.FirstOrDefault(s => a >= s.start && a < s.end).name ?? "?";

string Inspect(System.Reflection.MethodBase m)
{
    var f = Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m);
    if (f == null) return $"{m.DeclaringType.Name}.{m.Name}: no methodinfo field";
    IntPtr mi = (IntPtr)f.GetValue(null);
    IntPtr code = Marshal.ReadIntPtr(mi);
    var b = new byte[16]; Marshal.Copy(code, b, 0, 16);
    return $"{m.DeclaringType.Name}.{m.Name}: rva=0x{(long)code - baseAddr:X} sec={SectionOf((long)code)} bytes={BitConverter.ToString(b)}";
}

var sb = new System.Text.StringBuilder();
foreach (var s in secs) sb.AppendLine($"section {s.name,-8} rva 0x{s.start - baseAddr:X}-0x{s.end - baseAddr:X}");
sb.AppendLine(Inspect(AccessTools.Method(typeof(TPS.Core.GameSystems), "Update")));
sb.AppendLine(Inspect(AccessTools.Method(typeof(TPS.Game.Inventory), "RegisterConsoleCommands")));
foreach (var t in new[] { typeof(TPS.Game.ExpeditionManager), typeof(TPS.Game.ExpeditionRewards.ExpeditionRewardScene), typeof(TPS.Game.Inventory) })
    foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
        if (!m.IsSpecialName && !m.IsGenericMethodDefinition && !m.Name.StartsWith("_"))
            try { sb.AppendLine(Inspect(m)); } catch (Exception e) { sb.AppendLine($"{t.Name}.{m.Name}: {e.GetType().Name}"); }
return sb.ToString();
