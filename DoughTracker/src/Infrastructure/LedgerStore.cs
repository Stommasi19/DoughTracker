using System.Linq.Expressions;
using System.Globalization;
using Application;
using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public sealed class LedgerStore(LedgerDbContext db) : ILedgerStore
{
    private IQueryable<Transaction> Owned(string owner) => db.Transactions.AsNoTracking()
        .Where(t => t.OwnerId == owner && t.Account.OwnerId == owner && t.Account.DeletedAt == null && t.RemovedAt == null);

    private static readonly Expression<Func<Transaction, LedgerTransaction>> Dto = t => new LedgerTransaction(
        t.Id.ToString(), t.AccountId.ToString(), t.Amount, t.Currency, t.Date, t.AuthorizedDate,
        t.Description, t.MerchantName, t.ProviderCategoryId, t.ProviderCategoryKey, t.CategoryConfidence,
        t.Classification, t.Pending, t.PendingTransactionId, t.PaymentChannel, t.ManualCategoryId, false);

    private static IQueryable<Transaction> Filter(IQueryable<Transaction> query, LedgerFilter filter)
    {
        if (filter.AccountId is { } account) query = query.Where(t => t.AccountId == account);
        if (filter.CategoryId is { } category) query = query.Where(t => (t.ManualCategoryId ?? t.ProviderCategoryId) == category);
        if (filter.Currency is { } currency) query = query.Where(t => t.Currency == currency);
        return query;
    }

    public Task<AccountDto[]> Accounts(string owner, CancellationToken ct) => db.Accounts.AsNoTracking()
        .Where(a => a.OwnerId == owner && a.DeletedAt == null).OrderBy(a => a.Name).ThenBy(a => a.Id)
        .Select(a => new AccountDto(a.Id, a.ConnectionId, a.Connection.InstitutionId, a.Connection.InstitutionName, a.Connection.Provider,
            a.Name, a.Mask, a.Type, a.Subtype, a.CurrentBalance, a.AvailableBalance, a.Currency,
            a.Connection.Status, a.Connection.LastSyncAt, a.Connection.LastErrorCode)).ToArrayAsync(ct);

    public Task<DemoCategory[]> Categories(CancellationToken ct) => db.Categories.AsNoTracking()
        .OrderBy(c => c.Name).Select(c => new DemoCategory(c.Id, c.Name)).ToArrayAsync(ct);

    public async Task<WorkspaceMetadata> Workspace(string owner, CancellationToken ct)
    {
        var dates = await Owned(owner).Select(t => new { t.Date.Year, t.Date.Month }).Distinct().ToArrayAsync(ct);
        var asOf = await Owned(owner).MaxAsync(t => (DateOnly?)t.Date, ct);
        var accounts = await Accounts(owner, ct);
        var currencies = (await Currencies(owner, LedgerFilter.Parse(null, null, null, null, null, null, null), ct))
            .Concat(accounts.Select(a => a.Currency)).Distinct().Order().ToArray();
        var currentMonth = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var months = dates.Select(d => new DateOnly(d.Year, d.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .Append(currentMonth).Distinct().OrderDescending().ToArray();
        return new(asOf?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? currentMonth,
            currencies.Contains("USD") ? "USD" : currencies.FirstOrDefault() ?? "USD", months, currencies,
            asOf, accounts.Any(a => a.Provider == "development"));
    }

    public async Task ValidateFilters(string owner, LedgerFilter filter, CancellationToken ct)
    {
        if (filter.AccountId is { } id && !await db.Accounts.AnyAsync(a => a.Id == id && a.OwnerId == owner && a.DeletedAt == null, ct))
            throw new LedgerRequestException("Account not found.", 404);
        if (filter.CategoryId is { } category && !await db.Categories.AnyAsync(c => c.Id == category, ct))
            throw new LedgerRequestException("Choose a valid category.");
    }

    public async Task<TransactionPage> Transactions(string owner, LedgerFilter filter, CancellationToken ct)
    {
        var query = Filter(Owned(owner), filter);
        if (filter.From is { } from) query = query.Where(t => t.Date >= from);
        if (filter.To is { } to) query = query.Where(t => t.Date < to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + filter.Search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(t => EF.Functions.ILike(t.MerchantName ?? "", pattern, "\\") ||
                EF.Functions.ILike(t.Description, pattern, "\\"));
        }
        var count = await query.CountAsync(ct);
        // ponytail: offset pages may shift during concurrent imports; use cursors if that becomes disruptive.
        var items = await query.OrderByDescending(t => t.Date).ThenBy(t => t.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).Select(Dto).ToArrayAsync(ct);
        return new(items, filter.Page, filter.PageSize, count);
    }

    public async Task<LedgerTransaction?> SetCategory(string owner, Guid id, string? category, CancellationToken ct)
    {
        var updated = await db.Transactions.Where(t => t.Id == id && t.OwnerId == owner &&
            t.Account.OwnerId == owner && t.Account.DeletedAt == null && t.RemovedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ManualCategoryId, category), ct);
        return updated == 0 ? null : await Owned(owner).Where(t => t.Id == id).Select(Dto).SingleAsync(ct);
    }

    public Task<LedgerTransaction[]> ReportRows(string owner, LedgerFilter filter, DateOnly from, DateOnly to, CancellationToken ct) =>
        Filter(Owned(owner), filter).Where(t => t.Date >= from && t.Date < to).Select(Dto).ToArrayAsync(ct);

    public Task<string[]> Currencies(string owner, LedgerFilter filter, CancellationToken ct) =>
        Filter(Owned(owner), filter).Select(t => t.Currency).Distinct().OrderBy(c => c).ToArrayAsync(ct);
}
