var s = TPS.Game.FinanceManager.Instance.MonthlySummary;
var sb = new System.Text.StringBuilder();
foreach (var p in s.GetType().GetFields())
{
    if (p.FieldType != typeof(long) && p.FieldType != typeof(int)) continue;
    var v = Convert.ToInt64(p.GetValue(s));
    if (v != 0) sb.Append($"{p.Name}={v} ");
}
return sb.ToString();
