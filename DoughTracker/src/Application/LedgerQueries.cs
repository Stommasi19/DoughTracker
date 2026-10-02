using System.Globalization;
using Domain;

namespace Application;

public record AccountDto(Guid Id, Guid ConnectionId, string InstitutionId, string InstitutionName, string Provider,
    string Name, string? Mask, string Type, string Subtype, decimal? CurrentBalance,
    decimal? AvailableBalance, string Currency, string ConnectionStatus,
    DateTimeOffset? LastSyncAt, string? LastErrorCode);
public record ItemsResult<T>(T[] Items);
public record WorkspaceMetadata(string DefaultMonth, string DefaultCurrency, string[] Months,
    string[] Currencies, DateOnly? AsOf, bool Seeded)
{
    public DemoInstitution[] ConnectableInstitutions { get; init; } = [];
}
public record TransactionPage(LedgerTransaction[] Items, int Page, int PageSize, int TotalCount);
public record ReportResult(DateOnly DateFrom, DateOnly DateTo, CurrencyReport[] Currencies);
public record CurrencyReport(string Currency, ExpenseReport Report);
public record SummaryResult(DateOnly DateFrom, DateOnly DateTo, DateOnly PreviousDateFrom,
    DateOnly PreviousDateTo, CurrencySummary[] Currencies);
public record CurrencySummary(string Currency, decimal Spending, decimal PreviousSpending,
    decimal? ChangePercent, decimal PendingSpending, decimal Refunds, decimal DailyAverage, int ExpenseCount);
public record SpendingResult(DateOnly DateFrom, DateOnly DateTo, CurrencySpending[] Currencies);
public record CurrencySpending(string Currency, SpendingGroup[] Categories, SpendingGroup[] Merchants,
    DatedSpending[] Daily, MonthSpending[] Trend);
public record DatedSpending(int Day, DateOnly Date, decimal Current, decimal Previous);

// This boundary keeps EF/PostgreSQL dependencies out of Application.
public interface ILedgerStore
{
    Task<AccountDto[]> Accounts(string owner, CancellationToken ct);
    Task<WorkspaceMetadata> Workspace(string owner, CancellationToken ct);
    Task<DemoCategory[]> Categories(CancellationToken ct);
    Task ValidateFilters(string owner, LedgerFilter filter, CancellationToken ct);
    Task<TransactionPage> Transactions(string owner, LedgerFilter filter, CancellationToken ct);
    Task<LedgerTransaction?> SetCategory(string owner, Guid id, string? category, CancellationToken ct);
    Task<LedgerTransaction[]> ReportRows(string owner, LedgerFilter filter, DateOnly from, DateOnly to, CancellationToken ct);
    Task<string[]> Currencies(string owner, LedgerFilter filter, CancellationToken ct);
}

public sealed class LedgerRequestException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

public record LedgerFilter(DateOnly? From, DateOnly? To, Guid? AccountId, string? CategoryId,
    string? Currency, string? Search, int Page, int PageSize, bool CalendarMonth)
{
    public static LedgerFilter Parse(string? month, string? dateFrom, string? dateTo,
        string? accountId, string? categoryId, string? currency, string? search,
        int page = 1, int pageSize = 20, bool report = false)
    {
        DateOnly? from = null, to = null;
        var monthly = month is not null || (report && dateFrom is null && dateTo is null);
        if (month is not null && (dateFrom is not null || dateTo is not null))
            throw new LedgerRequestException("Choose a month or a date range, not both.");
        if (monthly)
        {
            month ??= DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) || start.Year is < 2 or > 9998)
                throw new LedgerRequestException("Provide month as YYYY-MM between years 0002 and 9998.");
            from = start; to = start.AddMonths(1);
        }
        else if (dateFrom is not null || dateTo is not null)
        {
            if (!DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
                start >= end || start.Year < 2 || end.Year > 9998)
                throw new LedgerRequestException("Provide an increasing dateFrom/dateTo range as YYYY-MM-DD.");
            from = start; to = end;
        }
        if (report && (to!.Value.DayNumber - from!.Value.DayNumber > 366 ||
            from.Value.DayNumber < to.Value.DayNumber - from.Value.DayNumber))
            throw new LedgerRequestException("Report ranges must contain 1–366 days with room for previous history.");
        Guid? account = null;
        if (accountId is not null)
        {
            if (!Guid.TryParse(accountId, out var id)) throw new LedgerRequestException("Provide a valid account ID.");
            account = id;
        }
        if (currency is not null && (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z')))
            throw new LedgerRequestException("Currency must be a three-letter uppercase code.");
        search = search?.Trim();
        if (search?.Length > 120) throw new LedgerRequestException("Search must be 120 characters or fewer.");
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new LedgerRequestException("Page must be positive and pageSize between 1 and 100.");
        return new(from, to, account, categoryId, currency, search, page, pageSize, monthly);
    }
}

public sealed class LedgerQueries(ILedgerStore store)
{
    public Task<WorkspaceMetadata> Workspace(string owner, CancellationToken ct) => store.Workspace(owner, ct);
    public Task<AccountDto[]> Accounts(string owner, CancellationToken ct) => store.Accounts(owner, ct);
    public Task<DemoCategory[]> Categories(CancellationToken ct) => store.Categories(ct);
    public async Task<TransactionPage> Transactions(string owner, LedgerFilter filter, CancellationToken ct)
    {
        await store.ValidateFilters(owner, filter, ct);
        return await store.Transactions(owner, filter, ct);
    }
    public async Task<LedgerTransaction> SetCategory(string owner, Guid id, string? category, CancellationToken ct)
    {
        if (category is not null && !(await store.Categories(ct)).Any(c => c.Id == category))
            throw new LedgerRequestException("Choose a valid category.");
        return await store.SetCategory(owner, id, category, ct) ??
            throw new LedgerRequestException("Transaction not found.", 404);
    }
    public async Task<ReportResult> Report(string owner, LedgerFilter filter, CancellationToken ct)
    {
        await store.ValidateFilters(owner, filter, ct);
        var from = filter.From!.Value;
        var to = filter.To!.Value;
        var prior = filter.CalendarMonth ? from.AddMonths(-1) : from.AddDays(-(to.DayNumber - from.DayNumber));
        var trendStart = new DateOnly(from.Year, from.Month, 1).AddMonths(-5);
        var trendEnd = new DateOnly(from.Year, from.Month, 1).AddMonths(1);
        var rows = await store.ReportRows(owner, filter, prior < trendStart ? prior : trendStart,
            to > trendEnd ? to : trendEnd, ct);
        var currencies = filter.Currency is null ? await store.Currencies(owner, filter, ct) : [filter.Currency];
        var categories = await store.Categories(ct);
        return new(from, to, currencies.Select(currency => new CurrencyReport(currency,
            SpendingReport.Calculate(rows.Where(t => t.Currency == currency).ToArray(), categories,
                from, to, prior))).ToArray());
    }
}
