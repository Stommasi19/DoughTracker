using Domain;

namespace Application;

public static class SpendingReport
{
    public static ExpenseReport Calculate(LedgerTransaction[] rows, DemoCategory[] categories,
        DateOnly from, DateOnly to, DateOnly previousFrom)
    {
        // ponytail: O(days × rows), at most 366 chart days; use running sums if report latency warrants it.
        var days = to.DayNumber - from.DayNumber;
        var priorDays = from.DayNumber - previousFrom.DayNumber;
        var expenses = rows.Where(t => !t.Removed && t.Classification == "expense" && !t.Pending).ToArray();
        var current = expenses.Where(t => t.Date >= from && t.Date < to).ToArray();
        var previous = expenses.Where(t => t.Date >= previousFrom && t.Date < from).ToArray();
        var spending = -current.Sum(t => t.Amount);
        var prior = -previous.Sum(t => t.Amount);
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        return new(spending, prior, prior == 0 ? null : decimal.Round((spending - prior) / Math.Abs(prior) * 100, 1),
            -rows.Where(t => !t.Removed && t.Classification == "expense" && t.Pending && t.Date >= from && t.Date < to).Sum(t => t.Amount),
            current.Where(t => t.Amount > 0).Sum(t => t.Amount), decimal.Round(spending / days, 2),
            current.Count(t => t.Amount < 0),
            current.GroupBy(t => t.CategoryId).Select(g => new SpendingGroup(g.Key,
                categories.FirstOrDefault(c => c.Id == g.Key)?.Name ?? "Uncategorized", -g.Sum(t => t.Amount), g.Count()))
                .OrderByDescending(g => g.Amount).ThenBy(g => g.Id).ToArray(),
            current.GroupBy(t => t.Merchant).Select(g => new SpendingGroup(g.Key, g.Key, -g.Sum(t => t.Amount), g.Count()))
                .OrderByDescending(g => g.Amount).ThenBy(g => g.Id).Take(5).ToArray(),
            Enumerable.Range(1, days).Select(day => new DailySpending(day,
                -current.Where(t => t.Date < from.AddDays(day)).Sum(t => t.Amount),
                -previous.Where(t => t.Date < previousFrom.AddDays(day == days ? priorDays : Math.Min(day, priorDays))).Sum(t => t.Amount))).ToArray(),
            Enumerable.Range(0, 6).Select(n => firstMonth.AddMonths(n - 5)).Select(month => new MonthSpending(
                month.ToString("yyyy-MM"), -expenses.Where(t => t.Date >= month && t.Date < month.AddMonths(1)).Sum(t => t.Amount))).ToArray());
    }
}
