// Where do the detours on our hooked methods currently jump to? (FF 25 = jmp [rip+disp32] -> absolute target)
using System.Runtime.InteropServices;
var mod = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().First(m => m.ModuleName == "GameAssembly.dll");
string Info(System.Reflection.MethodBase m)
{
    var mi = (IntPtr)Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m).GetValue(null);
    var code = Marshal.ReadIntPtr(mi);
    var b = new byte[16]; Marshal.Copy(code, b, 0, 16);
    string target = "";
    if (b[0] == 0xFF && b[1] == 0x25) { int disp = BitConverter.ToInt32(b, 2); var slot = code + 6 + disp; target = $" -> 0x{Marshal.ReadIntPtr(slot).ToInt64():X}"; }
    else if (b[0] == 0xE9) { int rel = BitConverter.ToInt32(b, 1); target = $" -> 0x{(code + 5 + rel).ToInt64():X}"; }
    return $"{m.DeclaringType.Name}.{m.Name} @0x{code.ToInt64():X} [{BitConverter.ToString(b, 0, 8)}]{target}";
}
var f = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
return string.Join("\n", new System.Reflection.MethodBase[] {
    typeof(TPS.Game.UI.InboxMenu_Message_Generic).GetMethod("Open", f),
    typeof(TPS.Game.UI.InboxMenu_Message).GetMethod("Open", f),
    typeof(TPS.Game.InboxMessage).GetMethod("OnChoice", f) }.Select(Info));
