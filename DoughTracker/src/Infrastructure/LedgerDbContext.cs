using Application;
using Domain;
using Microsoft.EntityFrameworkCore;
using MassTransit;

namespace Infrastructure;

public class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<FinancialConnection> Connections => Set<FinancialConnection>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddInboxStateEntity();
        model.AddOutboxMessageEntity();
        model.AddOutboxStateEntity();
        var run = model.Entity<SyncRun>();
        run.ToTable("sync_runs");
        run.HasKey(r => r.Id);
        run.HasOne<FinancialConnection>().WithMany().HasForeignKey(r => r.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        run.HasIndex(r => r.ConnectionId).IsUnique().HasFilter("\"Status\" IN ('requested', 'processing')");
        var connection = model.Entity<FinancialConnection>();
        connection.ToTable("financial_connections");
        connection.HasKey(c => c.Id);
        connection.HasAlternateKey(c => new { c.Id, c.OwnerId });
        connection.HasIndex(c => new { c.Provider, c.ProviderItemId }).IsUnique();
        connection.Property(c => c.OwnerId).HasMaxLength(128);
        var account = model.Entity<Account>();
        account.ToTable("accounts");
        account.HasKey(a => a.Id);
        account.HasAlternateKey(a => new { a.Id, a.OwnerId });
        account.HasOne(a => a.Connection).WithMany().HasForeignKey(a => new { a.ConnectionId, a.OwnerId })
            .HasPrincipalKey(c => new { c.Id, c.OwnerId }).OnDelete(DeleteBehavior.Restrict);
        account.HasIndex(a => new { a.ConnectionId, a.ProviderAccountId }).IsUnique();
        account.HasIndex(a => a.OwnerId);
        account.Property(a => a.OwnerId).HasMaxLength(128);
        account.Property(a => a.Currency).HasMaxLength(3);
        account.Property(a => a.CurrentBalance).HasPrecision(19, 4);
        account.Property(a => a.AvailableBalance).HasPrecision(19, 4);
        var category = model.Entity<Category>();
        category.ToTable("categories");
        category.HasKey(c => c.Id);
        category.HasData(DemoWorkspace.Categories.Select(c => new Category { Id = c.Id, Name = c.Name }));
        var transaction = model.Entity<Transaction>();
        transaction.ToTable("transactions");
        transaction.HasKey(t => t.Id);
        transaction.HasOne(t => t.Account).WithMany().HasForeignKey(t => new { t.AccountId, t.OwnerId })
            .HasPrincipalKey(a => new { a.Id, a.OwnerId }).OnDelete(DeleteBehavior.Restrict);
        transaction.HasOne<Category>().WithMany().HasForeignKey(t => t.ProviderCategoryId).OnDelete(DeleteBehavior.Restrict);
        transaction.HasOne<Category>().WithMany().HasForeignKey(t => t.ManualCategoryId).OnDelete(DeleteBehavior.Restrict);
        transaction.HasIndex(t => new { t.AccountId, t.ProviderTransactionId }).IsUnique();
        transaction.HasIndex(t => new { t.OwnerId, t.Date, t.Id });
        transaction.Property(t => t.OwnerId).HasMaxLength(128);
        transaction.Property(t => t.Amount).HasPrecision(19, 4);
        transaction.Property(t => t.Currency).HasMaxLength(3);
    }
}
