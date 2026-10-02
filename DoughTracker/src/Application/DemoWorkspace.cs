using System.Globalization;
using Domain;

namespace Application;

public class DemoWorkspace(DemoSeed seed)
{
    public static readonly DemoCategory[] Categories = [
        new("housing", "Housing"), new("groceries", "Groceries"), new("dining", "Food & drink"),
        new("shopping", "Shopping"), new("transport", "Transport"),
        new("entertainment", "Entertainment"), new("utilities", "Utilities"),
        new("health", "Health"), new("uncategorized", "Uncategorized")
    ];

    // ponytail: one demo-wide lock; use database transactions when persistent users arrive.
    private readonly object gate = new();
    private readonly Dictionary<string, LedgerTransaction> transactions = seed.Transactions
        .Where(t => seed.Institutions.Any(i => i.InitiallyConnected &&
            seed.Accounts.Any(a => a.Id == t.AccountId && a.InstitutionId == i.Id)))
        .ToDictionary(t => t.Id);
    private readonly Dictionary<string, DemoConnection> connections = seed.Institutions
        .Where(i => i.InitiallyConnected).ToDictionary(i => i.Id,
            i => new DemoConnection(i.Id, "connected", new DateTimeOffset(
                seed.AsOf.ToDateTime(new TimeOnly(17, 42)), TimeSpan.Zero)));

    public DemoSeed Seed => seed;

    public bool Connect(string institutionId)
    {
        lock (gate)
        {
            if (!seed.Institutions.Any(i => i.Id == institutionId)) return false;
            if (!connections.ContainsKey(institutionId))
            {
                var accountIds = seed.Accounts.Where(a => a.InstitutionId == institutionId)
                    .Select(a => a.Id).ToHashSet();
                foreach (var transaction in seed.Transactions.Where(t => accountIds.Contains(t.AccountId)))
                    transactions.TryAdd(transaction.Id, transaction);
            }
            connections[institutionId] = new(institutionId, "connected", DateTimeOffset.UtcNow);
            return true;
        }
    }

    public bool Disconnect(string institutionId)
    {
        lock (gate)
        {
            if (!connections.TryGetValue(institutionId, out var connection)) return false;
            connections[institutionId] = connection with { Status = "disconnected" };
            return true;
        }
    }

    public bool SetCategory(string transactionId, string? categoryId)
    {
        lock (gate)
        {
            if (!transactions.TryGetValue(transactionId, out var transaction) || transaction.Removed)
                return false;
            transactions[transactionId] = transaction with { ManualCategoryId = categoryId };
            return true;
        }
    }

    public bool ApplyBatch(string institutionId, LedgerBatch batch)
    {
        lock (gate)
        {
            if (!connections.TryGetValue(institutionId, out var connection) || connection.Status != "connected")
                return false;
            foreach (var incoming in batch.Added.Concat(batch.Modified))
            {
                transactions.TryGetValue(incoming.Id, out var existing);
                LedgerTransaction? pending = null;
                if (incoming.PendingTransactionId is { } pendingId)
                    transactions.TryGetValue(pendingId, out pending);
                transactions[incoming.Id] = incoming with {
                    ManualCategoryId = existing?.ManualCategoryId ?? pending?.ManualCategoryId
                };
                if (pending is not null) transactions[pending.Id] = pending with { Removed = true };
            }
            foreach (var id in batch.Removed)
                if (transactions.TryGetValue(id, out var removed))
                    transactions[id] = removed with { Removed = true };
            connections[institutionId] = connection with { LastSyncAt = DateTimeOffset.UtcNow };
            return true;
        }
    }

    public DemoSnapshot Snapshot(string month, string? accountId = null, string? categoryId = null,
        string? search = null)
    {
        lock (gate)
        {
            var start = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = start.AddMonths(1);
            var available = transactions.Values.Where(t => !t.Removed);
            if (!string.IsNullOrEmpty(accountId)) available = available.Where(t => t.AccountId == accountId);
            if (!string.IsNullOrEmpty(categoryId)) available = available.Where(t => t.CategoryId == categoryId);
            if (!string.IsNullOrWhiteSpace(search)) available = available.Where(t =>
                t.Merchant.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
            var all = available.ToArray();
            var current = all.Where(t => t.Date >= start && t.Date < end).ToArray();
            var expenses = current.Where(t => t.Classification == "expense" && !t.Pending).ToArray();
            var previous = all.Where(t => t.Date >= start.AddMonths(-1) && t.Date < start &&
                t.Classification == "expense" && !t.Pending).ToArray();
            var spending = -expenses.Sum(t => t.Amount);
            var previousSpending = -previous.Sum(t => t.Amount);
            var months = Enumerable.Range(0, 6).Select(n => seed.AsOf.AddMonths(-n).ToString("yyyy-MM")).ToArray();
            var days = DateTime.DaysInMonth(start.Year, start.Month);
            var priorDays = DateTime.DaysInMonth(start.AddMonths(-1).Year, start.AddMonths(-1).Month);
            var report = new ExpenseReport(spending, previousSpending,
                previousSpending == 0 ? null : decimal.Round((spending - previousSpending) / Math.Abs(previousSpending) * 100, 1),
                -current.Where(t => t.Classification == "expense" && t.Pending).Sum(t => t.Amount),
                expenses.Where(t => t.Amount > 0).Sum(t => t.Amount), decimal.Round(spending / days, 2),
                expenses.Count(t => t.Amount < 0),
                expenses.GroupBy(t => t.CategoryId).Select(g => new SpendingGroup(g.Key,
                    Categories.FirstOrDefault(c => c.Id == g.Key)?.Name ?? "Uncategorized",
                    -g.Sum(t => t.Amount), g.Count())).OrderByDescending(g => g.Amount).ToArray(),
                expenses.GroupBy(t => t.Merchant).Select(g => new SpendingGroup(g.Key, g.Key,
                    -g.Sum(t => t.Amount), g.Count())).OrderByDescending(g => g.Amount).Take(5).ToArray(),
                Enumerable.Range(1, days).Select(day => new DailySpending(day,
                    -expenses.Where(t => t.Date.Day <= day).Sum(t => t.Amount),
                    -previous.Where(t => t.Date.Day <= (day == days ? priorDays : Math.Min(day, priorDays))).Sum(t => t.Amount))).ToArray(),
                months.Reverse().Select(m => new MonthSpending(m,
                    -all.Where(t => t.Date.ToString("yyyy-MM") == m && t.Classification == "expense" && !t.Pending)
                        .Sum(t => t.Amount))).ToArray());
            return new(seed.AsOf, month, months, Categories, seed.Institutions,
                connections.Values.ToArray(), seed.Accounts.Where(a => connections.ContainsKey(a.InstitutionId)).ToArray(),
                current.OrderByDescending(t => t.Date).ThenBy(t => t.Id).ToArray(), report);
        }
    }
}
