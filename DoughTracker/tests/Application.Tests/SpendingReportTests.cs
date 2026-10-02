using Application;
using Domain;

public class SpendingReportTests
{
    [Fact]
    public void Refunds_exclusions_empty_windows_and_calendar_boundaries_agree()
    {
        var from = new DateOnly(2026, 9, 1);
        LedgerTransaction Row(string id, decimal amount, string classification = "expense", bool pending = false,
            bool removed = false, string category = "shopping") => new(id, "account", amount, "USD", from,
                null, id, null, category, null, null, classification, pending, null, "other", Removed: removed);
        var rows = new[] {
            Row("purchase", -100), Row("refund", 30), Row("refund-only", 10, category: "health"),
            Row("payroll", 500, "income"), Row("transfer", -200, "transfer"), Row("payment", -100, "card-payment"),
            Row("pending", -15, pending: true), Row("removed", -999, removed: true),
            Row("prior-final-day", -80) with { Date = new DateOnly(2026, 8, 31) }
        };
        var report = SpendingReport.Calculate(rows, DemoWorkspace.Categories, from, from.AddMonths(1), from.AddMonths(-1));
        Assert.Equal(60m, report.Spending);
        Assert.Equal(40m, report.Refunds);
        Assert.Equal(15m, report.PendingSpending);
        Assert.Equal(1, report.ExpenseCount);
        Assert.Equal(2m, report.DailyAverage);
        Assert.Equal(80m, report.PreviousSpending);
        Assert.Equal(-25m, report.ChangePercent);
        Assert.Equal(report.Spending, report.Categories.Sum(c => c.Amount));
        Assert.Equal(-10m, report.Categories.Single(c => c.Id == "health").Amount);
        Assert.Equal(report.Spending, report.Daily.Last().Current);
        Assert.Equal(report.PreviousSpending, report.Daily.Last().Previous);
        var empty = SpendingReport.Calculate([], DemoWorkspace.Categories, from, from.AddMonths(1), from.AddMonths(-1));
        Assert.Equal(0m, empty.Spending);
        Assert.Null(empty.ChangePercent);
        Assert.Equal(30, empty.Daily.Length);
        Assert.All(empty.Daily, d => Assert.Equal(0m, d.Current));
        Assert.Equal(6, empty.Trend.Length);
        Assert.Throws<LedgerRequestException>(() => LedgerFilter.Parse("9999-12", null, null, null, null, null, null, report: true));
        Assert.Throws<LedgerRequestException>(() => LedgerFilter.Parse(null, "0002-01-01", "0003-01-02", null, null, null, null, report: true));
    }
}
