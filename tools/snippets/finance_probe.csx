// Finance model check: balance, month-to-date vs last month, wages, loans, calendar.
var fm = TPS.Game.FinanceManager.Instance;
var tl = TPS.Game.Timeline.Instance;
var sb = new System.Text.StringBuilder();
sb.AppendLine($"balance={fm.Balance} canAfford(18000)={fm.CanAfford(18000)} wageMult={fm.StaffWageMultiplier} yearlyWages={fm.YearlyWages}");
sb.AppendLine($"date day={tl.Day} month={tl.Month} year={tl.Year} daysPerMonth={TPS.Game.Timeline.DaysPerMonth} monthsPerYear={TPS.Game.Timeline.MonthsPerYear}");
void S(string name, TPS.Game.FinanceManager.Summary s) =>
    sb.AppendLine($"{name}: m={s.Month} y={s.Year} profit={s.Profit} in={s.MoneyIn} out={s.MoneyOut} wages={s.Wages} loanRepay={s.LoanRepayments} loanInt={s.LoanInterest}");
S("MonthlySummary", fm.MonthlySummary);
S("LastMonthlySummary", fm.LastMonthlySummary);
sb.AppendLine($"monthToDate earned={fm.MonthlyEarnedRevenue} other={fm.MonthlyOtherRevenue} regularExp={fm.MonthlyRegularExpenses} sporadicExp={fm.MonthlySporadicExpenses}");
long salarySum = 0; int staffCount = 0;
foreach (var s in TPS.Game.StaffManager.Instance.Staff) { salarySum += s.Salary; staffCount++; }
sb.AppendLine($"staff={staffCount} sum(Salary)={salarySum} sum/12={salarySum / 12}");
foreach (var l in TPS.Game.LoanManager.Instance.Offers)
    if (l.Active) sb.AppendLine($"loan active repay={l.MonthlyRepayment} interest={l.MonthlyInterest} outstanding={l.OutstandingBalance} months={l.RemainingMonths}");
return sb.ToString();
