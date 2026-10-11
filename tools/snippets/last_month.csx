// Last month's finance summary, every field.
var s = TPS.Game.FinanceManager.Instance.LastMonthlySummary;
var sb = new System.Text.StringBuilder();
foreach (var p in s.GetType().GetFields())
{
    if (p.FieldType != typeof(long) && p.FieldType != typeof(int)) continue;
    object v; try { v = p.GetValue(s); } catch { continue; }
    if (Convert.ToInt64(v) != 0) sb.Append($"{p.Name}={v} ");
}
return sb.ToString();
