using Application;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure;

public static class ConnectionInfrastructure
{
    public static IServiceCollection AddConnections(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        if (config.GetValue<bool>("Plaid:Enabled"))
        {
            if (!config.GetValue<bool>("Messaging:Enabled")) throw new InvalidOperationException("Plaid requires durable messaging.");
            if (string.IsNullOrWhiteSpace(config["Plaid:ClientId"]) || string.IsNullOrWhiteSpace(config["Plaid:Secret"]))
                throw new InvalidOperationException("Configure backend Plaid Sandbox credentials.");
            if (!environment.IsDevelopment()) throw new InvalidOperationException("This release supports local Plaid Sandbox only.");
        }
        services.AddHttpClient<PlaidClient>(http => {
            http.BaseAddress = new Uri("https://sandbox.plaid.com/");
            http.Timeout = TimeSpan.FromSeconds(20);
        }).RemoveAllLoggers();
        services.AddScoped<ConnectionStore>();
        services.AddScoped<IConnectionStore>(p => p.GetRequiredService<ConnectionStore>());
        services.AddScoped<Connections>();
        services.AddSingleton<TokenSecrets>();
        services.AddSingleton<PlaidWebhookVerifier>();
        services.AddMemoryCache();
        var directory = TokenSecrets.DirectoryPath(config);
        if (config.GetValue<bool>("Plaid:Enabled") || config.GetValue<bool>("Messaging:Enabled"))
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            services.AddDataProtection().SetApplicationName("DoughTracker").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
        }
        services.AddMassTransit(bus => {
            bus.AddConsumer<SyncConsumer>();
            bus.AddConsumer<SyncFaultConsumer>();
            bus.AddEntityFrameworkOutbox<LedgerDbContext>(outbox => {
                outbox.UsePostgres();
                outbox.IsolationLevel = System.Data.IsolationLevel.ReadCommitted;
                outbox.UseBusOutbox(options => {
                    if (!config.GetValue<bool>("Messaging:Enabled")) options.DisableDeliveryService();
                });
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });
            if (config.GetValue<bool>("Messaging:Enabled"))
                bus.UsingRabbitMq((context, transport) => {
                    transport.Host(config["RabbitMq:Host"] ?? "localhost", config.GetValue<ushort>("RabbitMq:Port", 5672), config["RabbitMq:VirtualHost"] ?? "/", host => {
                        host.Username(config["RabbitMq:Username"] ?? "doughtracker");
                        host.Password(config["RabbitMq:Password"] ?? "local-development-only");
                    });
                    transport.ReceiveEndpoint("doughtracker-sync", endpoint => {
                        endpoint.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
                        endpoint.UseEntityFrameworkOutbox<LedgerDbContext>(context);
                        endpoint.ConfigureConsumer<SyncConsumer>(context);
                    });
                    transport.ReceiveEndpoint("doughtracker-sync-fault", endpoint => {
                        endpoint.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
                        endpoint.ConfigureConsumer<SyncFaultConsumer>(context);
                    });
                });
            else bus.UsingInMemory((_, _) => { }); // Read-only/development fixture mode; sync requests are unavailable.
        });
        if (config.GetValue<bool>("Messaging:Enabled")) services.AddHostedService<ConnectionReconciliation>();
        return services;
    }
}

public sealed class SyncConsumer(ConnectionStore store) : IConsumer<SyncConnectionRequested>
{
    public Task Consume(ConsumeContext<SyncConnectionRequested> context) =>
        store.Sync(context.Message, context, context.CancellationToken);
}

public sealed class SyncFaultConsumer(ConnectionStore store) : IConsumer<Fault<SyncConnectionRequested>>
{
    public Task Consume(ConsumeContext<Fault<SyncConnectionRequested>> context) =>
        store.Failed(context.Message.Message, context.CancellationToken);
}

public sealed class ConnectionReconciliation(IServiceScopeFactory scopes, ILogger<ConnectionReconciliation> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var startup = true;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ConnectionStore>().Reconcile(null, startup, ct);
                startup = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Reconciliation unavailable: {ErrorType}", error.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
