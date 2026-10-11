// methodPointer vs virtualMethodPointer (Il2CppMethodInfo +0 / +8) for virtual methods we hook or tried to.
using System.Runtime.InteropServices;
var mod = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().First(m => m.ModuleName == "GameAssembly.dll");
long b = (long)mod.BaseAddress;
string Show(System.Reflection.MethodBase m)
{
    var f = Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m);
    var mi = (IntPtr)f.GetValue(null);
    long p0 = (long)Marshal.ReadIntPtr(mi, 0), p1 = (long)Marshal.ReadIntPtr(mi, 8);
    var bytes0 = new byte[6]; Marshal.Copy((IntPtr)p0, bytes0, 0, 6);
    var bytes1 = new byte[6]; if (p1 != 0) Marshal.Copy((IntPtr)p1, bytes1, 0, 6);
    return $"{m.DeclaringType.Name}.{m.Name}: methodPointer=rva 0x{p0 - b:X} [{BitConverter.ToString(bytes0)}] virtualMethodPointer=rva 0x{p1 - b:X} [{BitConverter.ToString(bytes1)}]";
}
var f = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
return string.Join("\n", new System.Reflection.MethodBase[] {
    typeof(TPS.Game.UI.InboxMenu_Message_Generic).GetMethod("Open", f),
    typeof(TPS.Game.UI.InboxMenu_Message).GetMethod("Open", f),
    typeof(TPS.Game.InboxMessage).GetMethod("OnChoice", f),
    typeof(TPS.Game.MessagePopup).GetMethod("OnChoice", f),
}.Select(Show));
