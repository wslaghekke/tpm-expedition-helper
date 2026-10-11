// Localised name fields of POI and location definitions.
IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src)
{
    var en = src.GetEnumerator();
    var ne = en.Cast<Il2CppSystem.Collections.IEnumerator>();
    while (ne.MoveNext()) yield return en.Current;
}
var sb = new System.Text.StringBuilder();
void Names(object o, string label)
{
    foreach (var p in o.GetType().GetProperties())
        if (p.PropertyType == typeof(TPS.Core.Localisation.LocalisedString) && !p.Name.StartsWith("_"))
        {
            string v; try { v = ((TPS.Core.Localisation.LocalisedString)p.GetValue(o)).Translation; } catch { v = "?"; }
            if (!string.IsNullOrEmpty(v) && v.Length < 80) sb.AppendLine($"  {label}.{p.Name} = '{v}'");
        }
}
foreach (var poi in Iter(TPS.Game.WorldMap.Instance.AllPOIs))
{
    var d = poi.Definition;
    if (!(d.name.Contains("SmallPrehistoryInventions") || d.name.Contains("Dredge_GreaterMarrows"))) continue;
    sb.AppendLine(d.name);
    Names(d, "poi");
    var loc = TPS.Game.WorldMap.Instance.Config.Locations.FirstOrDefault(l => l.POIs.Any(id => id.ID == ((TPS.Game.WorldMapLocationPOIDefinitionID)d).ID));
    if (loc != null) Names(loc, "location");
}
return sb.ToString();
