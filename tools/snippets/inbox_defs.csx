// Read-only: which types hold InboxMessageDefinition fields (plain base type = generic inbox messages).
var asm = typeof(TPS.Game.InboxMessages).Assembly;
var sb = new System.Text.StringBuilder();
foreach (var t in asm.GetTypes())
    foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        if (typeof(TPS.Game.InboxMessageDefinition).IsAssignableFrom(p.PropertyType) && !p.Name.StartsWith("_"))
            sb.AppendLine($"{t.Name}.{p.Name} : {p.PropertyType.Name}");
return sb.ToString();
