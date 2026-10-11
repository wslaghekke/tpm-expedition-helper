// Read-only: compare the simple properties of an item's base/U1/U2 definitions and show what differs.
// Usage: set `stem` (asset name without _U1/_U2) and the definition type (ExhibitItemDefinition for MI_*, GameItemDefinition for Item_*).
string stem = "Item_Exhibit_Digiverse_OfferingStatue";
var all = UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.GameItemDefinition>();
var levels = new[] { stem, stem + "_U1", stem + "_U2" }.Select(n => all.FirstOrDefault(d => d.name == n)).ToArray();
if (levels.Any(l => l == null)) return "not all levels loaded";
string Val(object v)
{
    if (v == null) return "null";
    if (v is UnityEngine.Object uo) return uo.name;
    if (v is TPS.Core.Localisation.LocalisedString ls) { try { return "\"" + ls.Translation + "\""; } catch { return "(loc)"; } }
    var t = v.GetType();
    if (t.IsPrimitive || t.IsEnum || v is string) return v.ToString();
    if (v is Il2CppSystem.Object io) { try { return io.ToString(); } catch { } }
    return v.ToString();
}
var sb = new System.Text.StringBuilder();
foreach (var p in typeof(TPS.Game.GameItemDefinition).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
{
    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
    if (p.PropertyType.IsInterface) continue; // reading interop interfaces reflectively can crash the game
    string[] vals;
    try { vals = levels.Select(l => Val(p.GetValue(l))).ToArray(); } catch { continue; }
    if (vals.Distinct().Count() > 1) sb.AppendLine($"{p.Name}: {string.Join("  |  ", vals)}");
}
return sb.ToString();
