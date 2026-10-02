using Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure;

public static class LedgerInfrastructure
{
    public static IServiceCollection AddLedger(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && (configuration.GetValue<bool>("DevelopmentSeed:Enabled") ||
            configuration.GetValue<bool>("Database:MigrateOnStartup")))
            throw new InvalidOperationException("Startup migrations and development seeding are allowed only in Development.");
        services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("DoughTracker") ?? "Host=localhost;Database=doughtracker;Username=doughtracker",
            npgsql => npgsql.CommandTimeout(10)));
        services.AddScoped<ILedgerStore, LedgerStore>();
        services.AddScoped<LedgerQueries>();
        return services;
    }

    public static async Task InitializeLedger(this IServiceProvider services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup") && !configuration.GetValue<bool>("DevelopmentSeed:Enabled")) return;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        if (configuration.GetValue<bool>("Database:MigrateOnStartup")) await db.Database.MigrateAsync();
        if (configuration.GetValue<bool>("DevelopmentSeed:Enabled"))
            await DevelopmentSeed.Apply(db, configuration.GetSection("DevelopmentSeed:Owners").Get<string[]>() ?? []);
    }

    public static async Task<bool> Ready(LedgerDbContext db, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if ((await db.Database.GetPendingMigrationsAsync(timeout.Token)).Any()) return false;
            await db.Connections.OrderBy(c => c.Id).Select(c => new { c.Id, c.Status, c.LastSyncAt }).Take(1).ToArrayAsync(timeout.Token);
            await db.Accounts.OrderBy(a => a.Id).Select(a => new { a.Id, a.Currency, a.DeletedAt }).Take(1).ToArrayAsync(timeout.Token);
            await db.Transactions.OrderBy(t => t.Id).Select(t => new { t.Id, t.ManualCategoryId, t.Amount }).Take(1).ToArrayAsync(timeout.Token);
            return await db.Categories.AnyAsync(timeout.Token);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }
}
