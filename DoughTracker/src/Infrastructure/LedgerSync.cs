using Application;
using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public static class LedgerSync
{
    // Call inside the connection's transaction/lock; the caller also commits cursor/status/outbox.
    public static async Task Apply(LedgerDbContext db, FinancialConnection connection, SyncBatch batch, CancellationToken ct)
    {
        var accounts = await db.Accounts.Where(a => a.ConnectionId == connection.Id).ToDictionaryAsync(a => a.ProviderAccountId, ct);
        foreach (var source in batch.Accounts)
        {
            if (!accounts.TryGetValue(source.Id, out var account))
            {
                account = new Account { Id = Guid.NewGuid(), OwnerId = connection.OwnerId,
                    ConnectionId = connection.Id, ProviderAccountId = source.Id };
                db.Accounts.Add(account);
                accounts.Add(source.Id, account);
            }
            if (account.DeletedAt is not null) continue;
            account.Name = source.Name; account.Mask = source.Mask; account.Type = source.Type;
            account.Subtype = source.Subtype; account.CurrentBalance = source.CurrentBalance;
            account.AvailableBalance = source.AvailableBalance; account.Currency = source.Currency;
        }
        var rows = await db.Transactions.Where(t => t.Account.ConnectionId == connection.Id)
            .ToDictionaryAsync(t => (t.AccountId, t.ProviderTransactionId), ct);
        foreach (var source in batch.Transactions.Added.Concat(batch.Transactions.Modified))
        {
            if (!accounts.TryGetValue(source.AccountId, out var account))
                throw new InvalidOperationException("Sync batch references an unknown account.");
            if (account.DeletedAt is not null) continue;
            rows.TryGetValue((account.Id, source.Id), out var row);
            if (source.PendingTransactionId is { } pendingId && rows.TryGetValue((account.Id, pendingId), out var pending))
            {
                if (row is null)
                {
                    // Keep the internal ID and override when pending becomes posted.
                    rows.Remove((account.Id, pendingId));
                    row = pending;
                    row.ProviderTransactionId = source.Id;
                    rows[(account.Id, source.Id)] = row;
                }
                else if (pending.Id != row.Id)
                {
                    row.ManualCategoryId ??= pending.ManualCategoryId;
                    pending.RemovedAt = DateTimeOffset.UtcNow;
                }
            }
            if (row is null)
            {
                row = new Transaction { Id = Guid.NewGuid(), OwnerId = connection.OwnerId,
                    AccountId = account.Id, ProviderTransactionId = source.Id };
                db.Transactions.Add(row);
                rows.Add((account.Id, source.Id), row);
            }
            row.Amount = source.Amount; row.Currency = source.Currency; row.Date = source.Date;
            row.AuthorizedDate = source.AuthorizedDate; row.Description = source.Description;
            row.MerchantName = source.MerchantName; row.ProviderCategoryId = source.ProviderCategoryId;
            row.ProviderCategoryKey = source.ProviderCategoryKey; row.CategoryConfidence = source.CategoryConfidence;
            row.Classification = source.Classification; row.Pending = source.Pending;
            row.PendingTransactionId = source.PendingTransactionId; row.PaymentChannel = source.PaymentChannel;
            row.RemovedAt = null;
        }
        var removed = batch.Transactions.Removed.ToHashSet();
        foreach (var row in rows.Values.Where(t => removed.Contains(t.ProviderTransactionId)))
            row.RemovedAt = DateTimeOffset.UtcNow;
        connection.SyncCursor = batch.NextCursor;
    }
}
