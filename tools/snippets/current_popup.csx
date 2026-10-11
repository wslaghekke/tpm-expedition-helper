// Read-only: the popup currently shown, and whether MessagePopup.OnChoice is safe to detour.
var q = TPS.Game.GameApp.Instance.MessageQueue;
var sb = new System.Text.StringBuilder();
var cur = q.CurrentMessage;
if (cur == null) sb.AppendLine("no current popup");
else
{
    var p = cur.TryCast<TPS.Game.MessagePopup>();
    sb.AppendLine($"current: {cur.GetIl2CppType().FullName} title='{cur.Title}' status={cur.CurrentStatus} hasResponse={cur._response != null}");
    sb.AppendLine($"  text='{cur.Text}'");
    if (p != null) sb.AppendLine($"  def={p.Definition?.GetIl2CppType().Name} ui={(p.UIInstance == null ? "null" : p.UIInstance.GetIl2CppType().Name)} queued={q.Messages.Count}");
}
var why = (Func<System.Reflection.MethodBase, string>)Host.Exports["NativeCodeCheck.WhyUnsafe"];
foreach (var m in new[] {
    typeof(TPS.Game.MessagePopup).GetMethod("OnChoice", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly),
    typeof(TPS.Game.Message).GetMethod("OnChoiceInternal") })
    sb.AppendLine($"hook check {m.DeclaringType.Name}.{m.Name}: {why(m) ?? "OK"}");
return sb.ToString();
