// Read-only: find who holds InboxMessages, then list its messages.
var asm = typeof(TPS.Game.InboxMessages).Assembly;
var sb = new System.Text.StringBuilder();
TPS.Game.InboxMessages inbox = null;
foreach (var t in asm.GetTypes())
{
    foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
    {
        if (p.PropertyType != typeof(TPS.Game.InboxMessages)) continue;
        sb.AppendLine($"holder: {t.FullName}.{p.Name} (static={p.GetMethod?.IsStatic})");
        if (inbox != null) continue;
        object target = null;
        if (!p.GetMethod.IsStatic)
        {
            var inst = t.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
            target = inst?.GetValue(null);
            if (target == null) continue;
        }
        inbox = (TPS.Game.InboxMessages)p.GetValue(target);
    }
}
if (inbox == null)
{
    var tray = UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.UI.InboxTrayUI>().FirstOrDefault(t => t._inbox != null);
    sb.AppendLine($"InboxTrayUI live: {tray != null}");
    inbox = tray?._inbox;
}
if (inbox == null) return sb.AppendLine("no live inbox found").ToString();
sb.AppendLine($"messages={inbox.Messages.Count} unread={inbox.UnreadMessageCount}");
foreach (var m in inbox.Messages)
{
    var def = m.Definition;
    sb.AppendLine($"- {m.GetIl2CppType().Name} read={m.HasBeenRead} title='{m.Title}' type={def?.MessageType?.name} timeout={def?.TimeoutInSeconds} defType={def?.GetIl2CppType().Name}");
    sb.AppendLine($"    text='{(m.Text ?? "").Replace("\n", " | ")}'");
}
return sb.ToString();
