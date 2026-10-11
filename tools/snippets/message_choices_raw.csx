// Read-only: raw bytes of CancelRoomDefinition.Choices elements, to work out the LocalisedString layout.
using System.Runtime.InteropServices;
var d = TPS.Game.GameApp.Instance.MessageQueue.Config.CancelRoomDefinition;
var field = Il2CppInterop.Runtime.IL2CPP.GetIl2CppField(Il2CppInterop.Runtime.Il2CppClassPointerStore<TPS.Game.MessagePopupDefinition>.NativeClassPtr, "Choices");
var arr = Marshal.ReadIntPtr(d.Pointer + (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field));
var lsClass = Il2CppInterop.Runtime.Il2CppClassPointerStore<TPS.Core.Localisation.LocalisedString>.NativeClassPtr;
uint align = 0;
int elemSize = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_value_size(lsClass, ref align);
var sb = new System.Text.StringBuilder();
sb.AppendLine($"len={Marshal.ReadInt64(arr, 24)} elemSize={elemSize}");
foreach (var name in new[] { "_dev", "_termID" })
    sb.AppendLine($"field {name} offset={Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(Il2CppInterop.Runtime.IL2CPP.GetIl2CppField(lsClass, name))}");
for (int i = 0; i < 2; i++)
{
    var b = new byte[elemSize]; Marshal.Copy(arr + 32 + i * elemSize, b, 0, elemSize);
    var dev = Il2CppInterop.Runtime.IL2CPP.Il2CppStringToManaged(Marshal.ReadIntPtr(arr + 32 + i * elemSize));
    var ls = new TPS.Core.Localisation.LocalisedString(dev);
    sb.AppendLine($"[{i}] {BitConverter.ToString(b)} dev='{dev}' viaTermCtor: term='{ls.Term}' termID={ls._termID} translation='{ls.Translation}'");
}
return sb.ToString();
