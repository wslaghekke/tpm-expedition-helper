// Read-only: popup definitions in MessagesConfig and their button labels.
// Choices is an array of LocalisedString structs { string _dev; int _termID } — the interop wrapper
// can't index struct arrays safely, so read the raw il2cpp array (32-byte header, 16-byte elements).
using System.Runtime.InteropServices;
var q = TPS.Game.GameApp.Instance.MessageQueue;
var cfg = q.Config;
var sb = new System.Text.StringBuilder();
sb.AppendLine($"queue current={(q.CurrentMessage == null ? "none" : q.CurrentMessage.GetIl2CppType().Name)}");
string Label(int termId) { var ls = new TPS.Core.Localisation.LocalisedString(); ls._termID = termId; return ls.Translation; }
void Def(string name, TPS.Game.MessagePopupDefinition d)
{
    if (d == null) { sb.AppendLine($"{name}: null"); return; }
    var arrPtr = Marshal.ReadIntPtr(d.Pointer + (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(Il2CppInterop.Runtime.IL2CPP.GetIl2CppField(Il2CppInterop.Runtime.Il2CppClassPointerStore<TPS.Game.MessagePopupDefinition>.NativeClassPtr, "Choices")));
    var labels = new List<string>();
    if (arrPtr != IntPtr.Zero)
    {
        long len = Marshal.ReadInt64(arrPtr, 24);
        for (int i = 0; i < len && i < 5; i++) labels.Add($"'{Label(Marshal.ReadInt32(arrPtr, 32 + i * 16 + 8))}'");
    }
    sb.AppendLine($"{name}: type={d.GetIl2CppType().Name} title='{d.Title.Translation}' default={d.DefaultChoice} modal={d.Modal} pause={d.PauseGame} prefab={(d.DialogPrefab != null)} choices=[{string.Join(", ", labels)}]");
}
Def("GeneralMessage", cfg.GeneralMessage);
Def("CancelRoomDefinition", cfg.CancelRoomDefinition);
Def("CancelBuildingDefinition", cfg.CancelBuildingDefinition);
Def("CancelWorkshopProjectDefinition", cfg.CancelWorkshopProjectDefinition);
Def("SellConfirmationGeneric", cfg.SellConfirmationGeneric);
return sb.ToString();
