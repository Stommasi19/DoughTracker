using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application;
using Domain;
using Infrastructure;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace API.IntegrationTests;

[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public class PostgresCollection;

public sealed class PostgresBrokerFactAttribute : PostgresFactAttribute
{
    public PostgresBrokerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOUGHTRACKER_TEST_RABBITMQ_PORT")))
            Skip = "Set DOUGHTRACKER_TEST_RABBITMQ_PORT to an isolated RabbitMQ with local test credentials.";
    }
}

[Collection("PostgreSQL")]
public class BankSynchronizationTests
{
    [Fact]
    public void Plaid_startup_requires_credentials_messaging_and_local_Sandbox_mode()
    {
        foreach (var (environment, messaging, credentials, expected) in new[] {
            ("Development", "false", "test", "durable messaging"),
            ("Development", "true", "", "Sandbox credentials"),
            ("Production", "true", "test", "local Plaid Sandbox only")
        })
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
                builder.UseEnvironment(environment);
                builder.UseSetting("Firebase:UseMockAuthentication", (environment == "Development").ToString());
                builder.UseSetting("Firebase:ProjectId", "test-project");
                builder.UseSetting("Plaid:Enabled", "true");
                builder.UseSetting("Messaging:Enabled", messaging);
                builder.UseSetting("Plaid:ClientId", credentials); builder.UseSetting("Plaid:Secret", credentials);
            });
            Assert.Contains(expected, Assert.Throws<InvalidOperationException>(() => factory.CreateClient()).Message);
        }
    }

    private static string Database => Environment.GetEnvironmentVariable("DOUGHTRACKER_TEST_DATABASE")!;
    private static LedgerDbContext Db() => new(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(Database).Options);
    private static async Task Reset()
    {
        Assert.StartsWith("doughtracker_tests", new NpgsqlConnectionStringBuilder(Database).Database);
        await using var db = Db();
        await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync();
    }

    [PostgresFact]
    public async Task Complete_batches_preserve_overrides_pending_identity_tombstones_and_atomic_cursor()
    {
        await Reset();
        await using var db = Db();
        var connection = new FinancialConnection { Id = Guid.NewGuid(), OwnerId = "owner", ProviderItemId = "fake", Provider = "fake" };
        db.Connections.Add(connection); await db.SaveChangesAsync();
        var source = PlaidDemoData.Initial("amex");
        var accounts = source.Accounts.Select(a => new DemoAccount(a.AccountId, "amex", a.Name, a.Mask, a.Type, a.Subtype,
            a.Balances.Current, a.Balances.Available, "USD")).ToArray();
        accounts = accounts.Append(accounts[0] with { Id = "sibling", Name = "Sibling savings", Type = "depository", Subtype = "savings" }).ToArray();
        async Task Apply(SyncBatch batch)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await ConnectionStore.Lock(db, connection.Id, default);
            await LedgerSync.Apply(db, connection, batch, default);
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        var initial = new SyncBatch(accounts, PlaidDemoData.Normalize(source), "initial");
        await Apply(initial);
        var count = await db.Transactions.CountAsync(); await Apply(initial);
        Assert.Equal(count, await db.Transactions.CountAsync());
        var pending = await db.Transactions.SingleAsync(t => t.ProviderTransactionId == "demo-pending-coffee");
        var internalId = pending.Id; pending.ManualCategoryId = "health";
        var modified = await db.Transactions.SingleAsync(t => t.ProviderTransactionId == "demo-unknown");
        modified.ManualCategoryId = "shopping"; await db.SaveChangesAsync();
        await Apply(new(accounts.Select(a => a with { CurrentBalance = 123.4567m }).ToArray(),
            PlaidDemoData.Normalize(PlaidDemoData.Update("amex")), "updated"));
        await Apply(new(accounts, PlaidDemoData.Normalize(PlaidDemoData.Update("amex")), "updated"));
        db.ChangeTracker.Clear(); connection = await db.Connections.SingleAsync();
        var posted = await db.Transactions.SingleAsync(t => t.ProviderTransactionId == "demo-posted-coffee");
        Assert.Equal(internalId, posted.Id); Assert.Equal("health", posted.ManualCategoryId); Assert.False(posted.Pending);
        Assert.Equal("shopping", (await db.Transactions.SingleAsync(t => t.ProviderTransactionId == "demo-unknown")).ManualCategoryId);
        Assert.NotNull((await db.Transactions.SingleAsync(t => t.ProviderTransactionId == "demo-reversed")).RemovedAt);
        var before = await db.Transactions.CountAsync();
        var invalid = initial.Transactions.Added[0] with { Id = "invalid", ProviderCategoryId = "nonexistent-category" };
        await Assert.ThrowsAsync<DbUpdateException>(() => Apply(new(accounts, new([invalid], [], []), "must-not-commit")));
        db.ChangeTracker.Clear(); connection = await db.Connections.SingleAsync();
        Assert.Equal("updated", connection.SyncCursor); Assert.Equal(before, await db.Transactions.CountAsync());
        var account = await db.Accounts.SingleAsync(a => a.ProviderAccountId == "demo-credit");
        using var provider = new FakePlaid();
        var secretDirectory = Path.Combine(Path.GetTempPath(), "doughtracker-delete-test-" + Guid.NewGuid());
        try
        {
            using var factory = Factory(provider, secretDirectory);
            using var client = await Client(factory, "owner");
            (await client.DeleteAsync($"/api/v1/accounts/{account.Id}")).EnsureSuccessStatusCode();
        }
        finally { if (Directory.Exists(secretDirectory)) Directory.Delete(secretDirectory, true); }
        db.ChangeTracker.Clear(); connection = await db.Connections.SingleAsync();
        await Apply(initial);
        Assert.Empty(await db.Transactions.ToArrayAsync());
        Assert.NotNull((await db.Accounts.SingleAsync(a => a.Id == account.Id)).DeletedAt);
        Assert.Null((await db.Accounts.SingleAsync(a => a.ProviderAccountId == "sibling")).DeletedAt);
        Assert.Equal("connected", connection.Status);
    }

    private static WebApplicationFactory<Program> Factory(FakePlaid provider, string secrets, bool broker = true, bool paused = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DoughTracker", Database);
            builder.UseSetting("Secrets:Directory", secrets);
            builder.UseSetting("Messaging:Enabled", broker.ToString());
            builder.UseSetting("RabbitMq:Host", "localhost");
            builder.UseSetting("RabbitMq:Port", Environment.GetEnvironmentVariable("DOUGHTRACKER_TEST_RABBITMQ_PORT") ?? "5672");
            builder.UseSetting("RabbitMq:Username", "doughtracker"); builder.UseSetting("RabbitMq:Password", "local-test-only");
            builder.UseSetting("Logging:LogLevel:Default", "Critical");
            builder.ConfigureServices(services => {
                if (paused) services.RemoveAll<IHostedService>();
                services.AddDataProtection().SetApplicationName("DoughTracker").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(secrets, "keys")));
                // Only this controlled HTTP client sees fake credentials; no Sandbox network request is possible.
                services.AddScoped(_ => new PlaidClient(new HttpClient(provider) { BaseAddress = new Uri("https://sandbox.plaid.com/") },
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                        ["Plaid:Enabled"] = "true", ["Plaid:ClientId"] = "test", ["Plaid:Secret"] = "test-secret"
                    }).Build()));
            });
        });
    private static async Task<HttpClient> Client(WebApplicationFactory<Program> factory, string owner)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/dev/token", new { uid = owner });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idToken").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token); return client;
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Sync(WebApplicationFactory<Program> factory, Guid id)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var run = await db.SyncRuns.SingleAsync(r => r.ConnectionId == id && r.Status == "requested");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<ConnectionStore>().Sync(new(id, run.Id), scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), default);
        await transaction.CommitAsync();
    }

    [PostgresFact]
    public async Task Link_exchange_sync_signed_webhooks_reconnect_and_lifecycle_use_owned_durable_state()
    {
        await Reset(); using var provider = new FakePlaid();
        var secrets = Path.Combine(Path.GetTempPath(), "doughtracker-secrets-test-" + Guid.NewGuid());
        Directory.CreateDirectory(secrets);
        try
        {
            using var factory = Factory(provider, secrets);
            using var owner = await Client(factory, "owner"); using var other = await Client(factory, "other");
            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/connections/link-token", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/connections/exchange-token", new { publicToken = "" })).StatusCode);
            var link = await Body(await owner.PostAsJsonAsync("/api/v1/connections/link-token", new { ownerId = "other" }));
            Assert.Equal("test-link", link.GetProperty("linkToken").GetString());
            Assert.Equal("owner", provider.LastLink.GetProperty("user").GetProperty("client_user_id").GetString());
            Assert.Equal(180, provider.LastLink.GetProperty("transactions").GetProperty("days_requested").GetInt32());
            provider.FailMetadata = true;
            var failedExchange = await owner.PostAsJsonAsync("/api/v1/connections/exchange-token", new { publicToken = "public-test" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failedExchange.StatusCode);
            Assert.DoesNotContain("test-access", await failedExchange.Content.ReadAsStringAsync());
            Assert.Single(Directory.GetFiles(secrets, "*.secret"));
            Assert.DoesNotContain("test-access", await File.ReadAllTextAsync(Directory.GetFiles(secrets, "*.secret")[0]));
            provider.FailMetadata = false;
            var linked = await Body(await owner.PostAsJsonAsync("/api/v1/connections/exchange-token", new { publicToken = "public-test", ownerId = "other" }));
            var id = linked.GetProperty("id").GetGuid(); Assert.Equal(1, provider.ExchangeCalls);
            Assert.Equal("requested", linked.GetProperty("syncStatus").GetString());
            Assert.Empty((await Body(await owner.GetAsync("/api/v1/accounts"))).GetProperty("items").EnumerateArray());
            Assert.Empty((await Body(await other.GetAsync("/api/v1/connections"))).GetProperty("items").EnumerateArray());
            foreach (var action in new[] { "sync", "reconnect", "reconnect/complete" })
                Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/connections/{id}/{action}", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/connections/{id}")).StatusCode);
            await Body(await owner.PostAsJsonAsync($"/api/v1/connections/{id}/sync", new { }));
            await using (var db = Db()) { Assert.Single(await db.SyncRuns.ToArrayAsync()); Assert.True(await db.Set<OutboxMessage>().AnyAsync()); }
            provider.MutatePagination = true;
            await Sync(factory, id);
            Assert.Equal(new string?[] { null, "page-one", null, "page-one" }, provider.Cursors);
            var rows = (await Body(await owner.GetAsync("/api/v1/transactions?pageSize=100"))).GetProperty("items");
            Assert.Equal(2, rows.GetArrayLength());
            Assert.Equal(2, (await Body(await owner.GetAsync("/api/v1/accounts"))).GetProperty("items").GetArrayLength());
            var editable = rows.EnumerateArray().Single(r => r.GetProperty("pending").GetBoolean()).GetProperty("id").GetGuid();
            (await owner.PatchAsJsonAsync($"/api/v1/transactions/{editable}/category", new { categoryId = "health" })).EnsureSuccessStatusCode();
            provider.Update = true;
            var webhook = JsonSerializer.SerializeToUtf8Bytes(new { item_id = "test-item", webhook_type = "TRANSACTIONS", webhook_code = "SYNC_UPDATES_AVAILABLE" });
            async Task<HttpResponseMessage> Notify(string signature, byte[]? bytes = null)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/plaid") { Content = new ByteArrayContent(bytes ?? webhook) };
                request.Headers.Add("Plaid-Verification", signature); return await anonymous.SendAsync(request);
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await Notify("invalid")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Notify(provider.Sign(webhook, -301))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Notify(provider.Sign(webhook, 3600))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Notify(provider.Sign(webhook), Encoding.UTF8.GetBytes("{}"))).StatusCode);
            var malformed = Encoding.UTF8.GetBytes("{");
            Assert.Equal(HttpStatusCode.BadRequest, (await Notify(provider.Sign(malformed), malformed)).StatusCode);
            await using (var db = Db()) { Assert.Equal(1, await db.SyncRuns.CountAsync()); }
            (await Notify(provider.Sign(webhook))).EnsureSuccessStatusCode();
            (await Notify(provider.Sign(webhook))).EnsureSuccessStatusCode();
            await Sync(factory, id);
            rows = (await Body(await owner.GetAsync("/api/v1/transactions?pageSize=100"))).GetProperty("items");
            Assert.Single(rows.EnumerateArray()); Assert.Equal(editable, rows[0].GetProperty("id").GetGuid());
            Assert.Equal("health", rows[0].GetProperty("manualCategoryId").GetString());
            await Body(await owner.PostAsJsonAsync($"/api/v1/connections/{id}/sync", new { }));
            provider.LoginRequired = true; await Sync(factory, id);
            var attention = (await Body(await owner.GetAsync("/api/v1/connections"))).GetProperty("items")[0];
            Assert.Equal("attention_required", attention.GetProperty("status").GetString());
            Assert.Equal("ITEM_LOGIN_REQUIRED", attention.GetProperty("lastErrorCode").GetString());
            await Body(await owner.PostAsJsonAsync($"/api/v1/connections/{id}/reconnect", new { }));
            Assert.True(provider.LastLink.TryGetProperty("access_token", out _)); Assert.False(provider.LastLink.TryGetProperty("products", out _));
            provider.LoginRequired = false;
            (await owner.PostAsJsonAsync($"/api/v1/connections/{id}/reconnect/complete", new { })).EnsureSuccessStatusCode(); await Sync(factory, id);
            Assert.Equal(1, provider.ExchangeCalls);
            // Restart keeps secret keys, access tokens, history and cursors usable.
            using (var restarted = Factory(provider, secrets))
            using (var restartedOwner = await Client(restarted, "owner"))
                await Body(await restartedOwner.PostAsJsonAsync($"/api/v1/connections/{id}/reconnect", new { }));
            provider.FailRemove = true;
            await Body(await owner.PostAsJsonAsync($"/api/v1/connections/{id}/sync", new { }));
            Guid queuedRun;
            await using (var db = Db()) queuedRun = (await db.SyncRuns.SingleAsync(r => r.Status == "requested")).Id;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.DeleteAsync($"/api/v1/connections/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/connections/{id}/sync", new { })).StatusCode);
            provider.FailRemove = false;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(secrets, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.DeleteAsync($"/api/v1/connections/{id}")).StatusCode); }
                finally { File.SetUnixFileMode(secrets, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
                await using var db = Db(); Assert.NotNull((await db.Connections.SingleAsync()).ProviderRevokedAt);
            }
            var removeCalls = provider.RemoveCalls;
            (await owner.DeleteAsync($"/api/v1/connections/{id}")).EnsureSuccessStatusCode();
            if (!OperatingSystem.IsWindows()) Assert.Equal(removeCalls, provider.RemoveCalls);
            Assert.Empty(Directory.GetFiles(secrets, "*.secret"));
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                await scope.ServiceProvider.GetRequiredService<ConnectionStore>().Sync(new(id, queuedRun), scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), default);
                await transaction.CommitAsync();
                Assert.Equal("disconnected", (await db.Connections.SingleAsync()).Status);
                Assert.Equal("cancelled", (await db.SyncRuns.SingleAsync(r => r.Id == queuedRun)).Status);
            }
            Assert.Single((await Body(await owner.GetAsync("/api/v1/transactions"))).GetProperty("items").EnumerateArray());
            var accountId = (await Body(await owner.GetAsync("/api/v1/accounts"))).GetProperty("items")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/accounts/{accountId}")).StatusCode);
            (await owner.DeleteAsync($"/api/v1/accounts/{accountId}")).EnsureSuccessStatusCode();
            Assert.Empty((await Body(await owner.GetAsync("/api/v1/transactions"))).GetProperty("items").EnumerateArray());
            Assert.Single((await Body(await owner.GetAsync("/api/v1/accounts"))).GetProperty("items").EnumerateArray());
            (await owner.DeleteAsync($"/api/v1/accounts/{accountId}")).EnsureSuccessStatusCode();
        }
        finally { Directory.Delete(secrets, true); }
    }

    [PostgresBrokerFact]
    public async Task Broker_worker_drains_persisted_commands_after_restart_and_serializes_duplicates()
    {
        await Reset(); using var provider = new FakePlaid();
        var secrets = Path.Combine(Path.GetTempPath(), "doughtracker-broker-test-" + Guid.NewGuid()); Directory.CreateDirectory(secrets);
        try
        {
            Guid id;
            using (var paused = Factory(provider, secrets, broker: true, paused: true))
            using (var owner = await Client(paused, "owner"))
            {
                id = (await Body(await owner.PostAsJsonAsync("/api/v1/connections/exchange-token", new { publicToken = "public-test" }))).GetProperty("id").GetGuid();
                await using var db = Db(); Assert.True(await db.Set<OutboxMessage>().AnyAsync());
            }
            provider.PauseNextSync = true;
            using var worker = Factory(provider, secrets, broker: true, paused: false); using var client = await Client(worker, "owner");
            await provider.SyncEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var started = System.Diagnostics.Stopwatch.StartNew();
            await Body(await client.PostAsJsonAsync($"/api/v1/connections/{id}/sync", new { }));
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), "Request coalescing must not wait on provider fetching.");
            provider.ContinueSync.TrySetResult();
            async Task WaitComplete()
            {
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    await using var db = Db();
                    if (await db.SyncRuns.AnyAsync(r => r.ConnectionId == id && r.Status == "completed") &&
                        !await db.SyncRuns.AnyAsync(r => r.ConnectionId == id && (r.Status == "requested" || r.Status == "processing"))) return;
                    await Task.Delay(200);
                }
                throw new Exception("Durable worker did not finish.");
            }
            await WaitComplete();
            await using (var db = Db()) {
                Assert.Equal(2, await db.Transactions.CountAsync()); Assert.True(await db.Set<InboxState>().AnyAsync());
                Assert.True(await db.SyncRuns.AnyAsync(r => r.Reason == "follow_up" && r.Status == "completed"));
            }
            using (var scope = worker.Services.CreateScope())
            {
                await using var db = Db(); var run = await db.SyncRuns.FirstAsync();
                await scope.ServiceProvider.GetRequiredService<IBus>().Publish(new SyncConnectionRequested(id, run.Id));
                await scope.ServiceProvider.GetRequiredService<IBus>().Publish(new SyncConnectionRequested(id, run.Id));
            }
            await Task.Delay(1000); await using (var db = Db()) Assert.Equal(2, await db.Transactions.CountAsync());
            // Queue reconciliation after an hour without relying on a remote notification.
            await using (var db = Db()) await db.Connections.ExecuteUpdateAsync(s => s.SetProperty(c => c.LastCheckedAt, DateTimeOffset.UtcNow.AddHours(-2)));
            using (var scope = worker.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<ConnectionStore>().Reconcile("owner", false, default);
            await WaitComplete();
            Assert.Equal(1, provider.MaxConcurrentSync);
        }
        finally { Directory.Delete(secrets, true); }
    }
}

internal sealed class FakePlaid : HttpMessageHandler
{
    public bool FailMetadata, MutatePagination, Update, LoginRequired, FailRemove;
    public int ExchangeCalls, MaxConcurrentSync, RemoveCalls;
    public bool PauseNextSync;
    public TaskCompletionSource SyncEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ContinueSync { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int concurrent;
    public JsonElement LastLink;
    public List<string?> Cursors { get; } = [];
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public string Sign(byte[] body, int age = 0)
    {
        var header = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = "test-key" }));
        var payload = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new {
            iat = DateTimeOffset.UtcNow.AddSeconds(age).ToUnixTimeSeconds(), request_body_sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant()
        }));
        return header + "." + payload + "." + WebEncoders.Base64UrlEncode(key.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var input = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("test", input.GetProperty("client_id").GetString());
        object output; var status = HttpStatusCode.OK;
        switch (request.RequestUri!.AbsolutePath)
        {
            case "/link/token/create": LastLink = input.Clone(); output = new { link_token = "test-link", expiration = DateTimeOffset.UtcNow.AddHours(1) }; break;
            case "/item/public_token/exchange": ExchangeCalls++; output = new { access_token = "test-access", item_id = "test-item" }; break;
            case "/item/get":
                if (FailMetadata) { status = HttpStatusCode.BadRequest; output = new { error_code = "ERROR", error_message = "must-never-leak-test-access" }; }
                else output = new { item = new { institution_id = "test-bank" } }; break;
            case "/institutions/get_by_id": output = new { institution = new { name = "Test Bank" } }; break;
            case "/accounts/get": output = new { accounts = PlaidDemoData.Initial("amex").Accounts
                .Concat(PlaidDemoData.Initial("chase").Accounts.Where(a => a.Subtype == "savings")).ToArray() }; break;
            case "/item/remove":
                RemoveCalls++;
                if (FailRemove) { status = HttpStatusCode.BadRequest; output = new { error_code = "ERROR" }; }
                else output = new { removed = true }; break;
            case "/webhook_verification_key/get":
                var parameters = key.ExportParameters(false);
                output = new { key = new { alg = "ES256", crv = "P-256", kty = "EC", kid = "test-key",
                    x = WebEncoders.Base64UrlEncode(parameters.Q.X!), y = WebEncoders.Base64UrlEncode(parameters.Q.Y!), expired_at = (long?)null } }; break;
            case "/transactions/sync":
                var active = Interlocked.Increment(ref concurrent); MaxConcurrentSync = Math.Max(active, MaxConcurrentSync);
                try
                {
                    var cursor = input.GetProperty("cursor").GetString(); lock (Cursors) Cursors.Add(cursor);
                    if (PauseNextSync) { PauseNextSync = false; SyncEntered.TrySetResult(); await ContinueSync.Task.WaitAsync(ct); }
                    await Task.Delay(20, ct);
                    if (LoginRequired) { status = HttpStatusCode.BadRequest; output = new { error_code = "ITEM_LOGIN_REQUIRED" }; break; }
                    if (MutatePagination && cursor == "page-one")
                    {
                        MutatePagination = false; status = HttpStatusCode.BadRequest;
                        output = new { error_code = "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" }; break;
                    }
                    var initial = PlaidDemoData.Initial("amex"); var pending = initial.Added.First(t => t.Pending);
                    var other = initial.Added.First(t => !t.Pending);
                    var posted = pending with { TransactionId = "posted", Pending = false, PendingTransactionId = pending.TransactionId };
                    output = new { accounts = initial.Accounts,
                        added = Update ? new[] { posted } : cursor is null ? new[] { pending } : cursor == "page-one" ? new[] { other } : [],
                        modified = Array.Empty<PlaidTransaction>(),
                        removed = Update ? new[] { new PlaidRemoved(pending.TransactionId, pending.AccountId), new PlaidRemoved(other.TransactionId, other.AccountId) } : [],
                        has_more = !Update && cursor is null, next_cursor = !Update && cursor is null ? "page-one" : Update ? "updated" : "saved" }; break;
                }
                finally { Interlocked.Decrement(ref concurrent); }
            default: throw new Exception("Unexpected provider route: " + request.RequestUri.AbsolutePath);
        }
        return new HttpResponseMessage(status) { Content = JsonContent.Create(output, options: Json) };
    }
    protected override void Dispose(bool disposing) { if (disposing) key.Dispose(); base.Dispose(disposing); }
}
