using System.Security.Cryptography;
using System.Text;
using Domain;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public static class DevelopmentSeed
{
    private static Guid Id(string owner, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(owner + ":" + key)).AsSpan(0, 16));

    public static async Task Apply(LedgerDbContext db, string[] owners, CancellationToken ct = default)
    {
        if (owners.Length != 2 || owners.Distinct().Count() != 2 || owners.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 128))
            throw new InvalidOperationException("Configure two distinct DevelopmentSeed:Owners UIDs of at most 128 characters.");
        var seed = PlaidDemoData.Seed();
        var stamp = new DateTimeOffset(seed.AsOf.ToDateTime(new TimeOnly(17, 42)), TimeSpan.Zero);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        for (var index = 0; index < owners.Length; index++)
        {
            var owner = owners[index];
            var connectionIds = await db.Connections.Where(c => c.OwnerId == owner).Select(c => c.Id).ToListAsync(ct);
            var accountIds = await db.Accounts.Where(a => a.OwnerId == owner).Select(a => a.Id).ToListAsync(ct);
            var transactionIds = (await db.Transactions.Where(t => t.OwnerId == owner).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
            foreach (var institution in seed.Institutions.Where(i => i.InitiallyConnected))
            {
                var id = Id(owner, "connection:" + institution.Id);
                if (!connectionIds.Contains(id)) db.Connections.Add(new FinancialConnection {
                    Id = id, OwnerId = owner, Provider = "development", ProviderItemId = owner + ":" + institution.Id,
                    InstitutionId = institution.Id, InstitutionName = institution.Name,
                    LastSyncAt = stamp, CreatedAt = stamp, UpdatedAt = stamp
                });
            }
            foreach (var account in seed.Accounts.Where(a => seed.Institutions.Any(i => i.Id == a.InstitutionId && i.InitiallyConnected)))
            {
                var id = Id(owner, "account:" + account.Id);
                if (!accountIds.Contains(id)) db.Accounts.Add(new Account {
                    Id = id, OwnerId = owner, ConnectionId = Id(owner, "connection:" + account.InstitutionId),
                    ProviderAccountId = account.Id, Name = index == 0 ? account.Name : "Second owner · " + account.Name,
                    Mask = account.Mask, Type = account.Type, Subtype = account.Subtype,
                    CurrentBalance = account.CurrentBalance, AvailableBalance = account.AvailableBalance, Currency = account.Currency
                });
            }
            var deletedId = Id(owner, "account:deleted");
            if (!accountIds.Contains(deletedId)) db.Accounts.Add(new Account {
                Id = deletedId, OwnerId = owner, ConnectionId = Id(owner, "connection:chase"),
                ProviderAccountId = "deleted", Name = "Deleted fixture account", Type = "depository", Subtype = "checking", DeletedAt = stamp
            });
            void Add(LedgerTransaction row, Guid? accountId = null)
            {
                var id = Id(owner, "transaction:" + row.Id);
                if (transactionIds.Contains(id)) return;
                db.Transactions.Add(new Transaction {
                    Id = id, OwnerId = owner, AccountId = accountId ?? Id(owner, "account:" + row.AccountId),
                    ProviderTransactionId = row.Id, Amount = index == 0 ? row.Amount : row.Amount * 0.5m,
                    Currency = row.Currency, Date = row.Date, AuthorizedDate = row.AuthorizedDate,
                    Description = row.Description, MerchantName = row.MerchantName, ProviderCategoryId = row.ProviderCategoryId,
                    ProviderCategoryKey = row.ProviderCategoryKey, CategoryConfidence = row.CategoryConfidence,
                    Classification = row.Classification, Pending = row.Pending, PendingTransactionId = row.PendingTransactionId,
                    PaymentChannel = row.PaymentChannel, ManualCategoryId = row.ManualCategoryId,
                    RemovedAt = row.Removed ? stamp : null
                });
            }
            foreach (var row in seed.Transactions.Where(t => seed.Accounts.Any(a => a.Id == t.AccountId &&
                seed.Institutions.Any(i => i.Id == a.InstitutionId && i.InitiallyConnected)))) Add(row);
            var extra = seed.Transactions.First(t => t.Classification == "expense") with {
                Date = seed.AsOf, AuthorizedDate = null, MerchantName = "Precision Market", Description = "Literal 100%_test\\purchase", Pending = false
            };
            Add(extra with { Id = "precision-eur", Amount = -12.3456m, Currency = "EUR" });
            Add(extra with { Id = "removed", Amount = -777m, Removed = true });
            Add(extra with { Id = "deleted-history", Amount = -888m }, deletedId);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
