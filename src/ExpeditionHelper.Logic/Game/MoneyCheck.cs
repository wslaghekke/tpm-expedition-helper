using TPS.Game;

namespace ExpeditionHelper.Logic.Game;

/// <summary>
/// Money rules for repeating. Model (verified in game): StaffInstance.Salary is yearly and wages are
/// paid once a month (last month's wage bill == Σ Salary / 12); loans charge MonthlyRepayment monthly;
/// the month-to-date summary doesn't include those yet, so profit is projected by subtracting them.
/// </summary>
internal static class MoneyCheck
{
    /// <summary>Projections are unreliable in the first days of a month; use last month's result then.</summary>
    private const int MinDaysForProjection = 5;

    public static long MonthEndBills()
    {
        double wages = 0;
        foreach (var staff in StaffManager.Instance.Staff) wages += staff.Salary;
        wages = wages / Timeline.MonthsPerYear * FinanceManager.Instance.StaffWageMultiplier;

        long loans = 0;
        foreach (var loan in LoanManager.Instance.Offers)
            if (loan.Active) loans += loan.MonthlyRepayment;
        return (long)wages + loans;
    }

    /// <summary>
    /// This month's expected result: month-to-date profit extrapolated to the whole month, plus the
    /// income that only arrives at month end (monthly grant/allowance, estimated from last month),
    /// minus the month-end bills (wages and loans aren't in the month-to-date summary yet).
    /// Timeline.Day is 0-based.
    /// </summary>
    public static long ProjectedProfit()
    {
        var finance = FinanceManager.Instance;
        int daysElapsed = Timeline.Instance.Day + 1;
        var last = finance.LastMonthlySummary;
        if (daysElapsed < MinDaysForProjection) return last.Profit;
        double fullMonth = (double)finance.MonthlySummary.Profit * Timeline.DaysPerMonth / daysElapsed;
        return (long)fullMonth + last.MonthlyGrant + last.MonthlyAllowance - MonthEndBills();
    }

    /// <summary>null when paying `cost` now is acceptable; otherwise why not.</summary>
    public static string WhyNotAffordable(int cost, Settings settings)
    {
        long cash = FinanceManager.Instance.Balance;
        long after = cash - cost;
        if (after < settings.MinCashReserve.Value)
            return $"cash would drop to {after:N0}, below the reserve of {settings.MinCashReserve.Value:N0}";
        if (settings.KeepMonthlyCostsCovered.Value)
        {
            long bills = MonthEndBills();
            if (after < bills) return $"{bills - after:N0} short of the month-end bills ({bills:N0})";
        }
        if (settings.OnlyWhenProfitable.Value)
        {
            long profit = ProjectedProfit();
            if (profit < 0) return $"museum is losing money (projected {profit:N0} this month)";
        }
        return null;
    }
}
