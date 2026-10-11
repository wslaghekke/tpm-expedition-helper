// Read-only: advisor message definitions in MessagesConfig.
var cfg = TPS.Game.GameApp.Instance.MessageQueue.Config;
var sb = new System.Text.StringBuilder();
foreach (var p in cfg.GetType().GetProperties())
{
    if (!typeof(TPS.Game.MessageAdvisorDefinition).IsAssignableFrom(p.PropertyType) || p.Name.StartsWith("_")) continue;
    var d = (TPS.Game.MessageAdvisorDefinition)p.GetValue(cfg);
    if (d == null) { sb.AppendLine($"{p.Name}: null"); continue; }
    sb.AppendLine($"{p.Name}: style={d.MessageStyle} title='{d.Title.Translation}' text='{d.Text.Translation}' displayTime={d.DisplayTime} hideDismiss={d.HideDismissButton} action={d.ShowActionButton} actionText='{d.ActionString.Translation}' whenPaused={d.ShowWhenGamePaused} charImage={(d.CharacterImage != null)} charPrefab={(d.CharacterPrefab != null)}");
}
var style = typeof(TPS.Game.EAdvisorMessageStyle);
sb.AppendLine("EAdvisorMessageStyle: " + string.Join(", ", Enum.GetNames(style)));
return sb.ToString();
