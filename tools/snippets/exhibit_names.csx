// Display names of exhibit definitions (internal name vs what the player sees).
var sb = new System.Text.StringBuilder();
foreach (var d in UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.ExhibitDefinition>())
{
    if (!(d.name.Contains("BonePhone") || d.name.Contains("WhackAMole") || d.name.Contains("BlueMackerel"))) continue;
    foreach (var p in d.GetType().GetProperties())
        if (p.PropertyType == typeof(TPS.Core.Localisation.LocalisedString) && !p.Name.StartsWith("_"))
        {
            string v; try { v = ((TPS.Core.Localisation.LocalisedString)p.GetValue(d)).Translation; } catch { v = "?"; }
            if (!string.IsNullOrEmpty(v) && v.Length < 60) sb.AppendLine($"{d.name}.{p.Name} = '{v}'");
        }
}
return sb.ToString();
