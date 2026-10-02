using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain;
using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace API.IntegrationTests;

public class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOUGHTRACKER_TEST_DATABASE")))
            Skip = "Set DOUGHTRACKER_TEST_DATABASE to a dedicated doughtracker_tests* PostgreSQL database.";
    }
}

[Collection("PostgreSQL")]
public class PersistentLedgerTests
{
    [Fact]
    public void Production_rejects_startup_migration_and_development_seed_flags()
    {
        foreach (var setting in new[] { "DevelopmentSeed:Enabled", "Database:MigrateOnStartup" })
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
                builder.UseEnvironment("Production");
                builder.UseSetting("Firebase:UseMockAuthentication", "false");
                builder.UseSetting("Firebase:ProjectId", "test-project");
                builder.UseSetting(setting, "true");
            });
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("allowed only in Development", error.Message);
        }
    }

    private static WebApplicationFactory<Program> Factory(string connection) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DoughTracker", connection);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("DevelopmentSeed:Enabled", "false");
            builder.UseSetting("Logging:LogLevel:Default", "Critical");
        });

    private static async Task<HttpClient> Client(WebApplicationFactory<Program> factory, string owner)
    {
        var client = factory.CreateClient();
        var token = await client.PostAsJsonAsync("/api/v1/dev/token", new { uid = owner });
        token.EnsureSuccessStatusCode();
        var body = await token.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("idToken").GetString());
        return client;
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [PostgresFact]
    public async Task Stored_ledger_is_isolated_filterable_durable_and_matches_reports()
    {
        var connection = Environment.GetEnvironmentVariable("DOUGHTRACKER_TEST_DATABASE")!;
        var configuration = new NpgsqlConnectionStringBuilder(connection);
        Assert.StartsWith("doughtracker_tests", configuration.Database);
        using var factory = Factory(connection);
        using var alice = await Client(factory, "alice");
        using var bob = await Client(factory, "bob");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        // This explicit test database is disposable; never target the development ledger.
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await alice.GetAsync("/health/ready")).StatusCode);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await DevelopmentSeed.Apply(db, ["alice", "bob"]);
        var initialCount = await db.Transactions.CountAsync();
        await DevelopmentSeed.Apply(db, ["alice", "bob"]);
        Assert.Equal(initialCount, await db.Transactions.CountAsync());
        Assert.Equal(-12.3456m, await db.Transactions.Where(t => t.OwnerId == "alice" && t.ProviderTransactionId == "precision-eur").Select(t => t.Amount).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/health/ready")).StatusCode);
        using var anonymous = factory.CreateClient();
        foreach (var endpoint in new[] { "/workspace", "/accounts", "/transactions", "/categories", "/reports/summary", "/reports/spending" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1" + endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/connections", new { institutionId = "capital-one" })).StatusCode);

        var accounts = (await Read(alice, "/api/v1/accounts")).GetProperty("items");
        var bobAccounts = (await Read(bob, "/api/v1/accounts")).GetProperty("items");
        Assert.Equal(3, accounts.GetArrayLength());
        var workspace = await Read(alice, "/api/v1/workspace");
        Assert.Equal("2026-09", workspace.GetProperty("defaultMonth").GetString());
        Assert.Equal("USD", workspace.GetProperty("defaultCurrency").GetString());
        Assert.Equal("2026-09-30", workspace.GetProperty("asOf").GetString());
        Assert.True(workspace.GetProperty("seeded").GetBoolean());
        Assert.Equal(new[] { "EUR", "USD" }, workspace.GetProperty("currencies").EnumerateArray().Select(c => c.GetString()));
        Assert.Contains(workspace.GetProperty("months").EnumerateArray(), m => m.GetString() == "2026-04");
        Assert.DoesNotContain(accounts.EnumerateArray(), a => bobAccounts.EnumerateArray().Any(b => b.GetProperty("id").GetString() == a.GetProperty("id").GetString()));
        var foreign = bobAccounts[0].GetProperty("id").GetString();
        foreach (var endpoint in new[] { "transactions", "reports/summary", "reports/spending" })
        {
            var unavailable = await alice.GetAsync($"/api/v1/{endpoint}?accountId={foreign}");
            var missing = await alice.GetAsync($"/api/v1/{endpoint}?accountId={Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
            Assert.Equal((await unavailable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(),
                (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        }
        foreach (var query in new[] { "page=0", "page=invalid", "pageSize=invalid", "pageSize=101", "page=2147483647&pageSize=100", "month=2026-13",
            "dateFrom=2026-09-02&dateTo=2026-09-01", "dateFrom=2026-09-01", "month=2026-09&dateTo=2026-10-01",
            "categoryId=unknown", "currency=usd", "accountId=invalid", "search=" + new string('x', 121) })
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync("/api/v1/transactions?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync("/api/v1/reports/summary?dateFrom=2026-01-01&dateTo=2028-01-01")).StatusCode);

        var all = (await Read(alice, "/api/v1/transactions?month=2026-09&currency=USD&pageSize=100")).GetProperty("items");
        var first = await Read(alice, "/api/v1/transactions?month=2026-09&currency=USD&pageSize=2");
        var second = await Read(alice, "/api/v1/transactions?month=2026-09&currency=USD&pageSize=2&page=2");
        Assert.Equal(all[0].GetProperty("id").GetString(), first.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(all[2].GetProperty("id").GetString(), second.GetProperty("items")[0].GetProperty("id").GetString());
        var searched = await Read(alice, "/api/v1/transactions?search=" + Uri.EscapeDataString("100%_test\\purchase"));
        Assert.Single(searched.GetProperty("items").EnumerateArray());
        Assert.Equal(0, (await Read(alice, "/api/v1/transactions?search=not-a-merchant")).GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(all.EnumerateArray(), t => t.GetProperty("amount").GetDecimal() is -777m or -888m);

        var summary = (await Read(alice, "/api/v1/reports/summary?month=2026-09&currency=USD")).GetProperty("currencies")[0];
        var spending = (await Read(alice, "/api/v1/reports/spending?month=2026-09&currency=USD")).GetProperty("currencies")[0];
        var expected = -all.EnumerateArray().Where(t => t.GetProperty("classification").GetString() == "expense" && !t.GetProperty("pending").GetBoolean()).Sum(t => t.GetProperty("amount").GetDecimal());
        Assert.Equal(expected, summary.GetProperty("spending").GetDecimal());
        Assert.Equal(39.90m, summary.GetProperty("refunds").GetDecimal());
        Assert.Equal(119.55m, summary.GetProperty("pendingSpending").GetDecimal());
        Assert.Equal(expected, spending.GetProperty("categories").EnumerateArray().Sum(c => c.GetProperty("amount").GetDecimal()));
        Assert.Equal(expected, spending.GetProperty("daily")[29].GetProperty("current").GetDecimal());
        var bobSummary = (await Read(bob, "/api/v1/reports/summary?month=2026-09&currency=USD")).GetProperty("currencies")[0];
        Assert.Equal(expected / 2, bobSummary.GetProperty("spending").GetDecimal());
        var eur = (await Read(alice, "/api/v1/reports/summary?month=2026-09&currency=EUR")).GetProperty("currencies")[0];
        Assert.Equal(12.3456m, eur.GetProperty("spending").GetDecimal());
        Assert.Equal(2, (await Read(alice, "/api/v1/reports/summary?month=2026-09")).GetProperty("currencies").GetArrayLength());
        var empty = (await Read(alice, "/api/v1/reports/summary?month=2027-01&currency=USD")).GetProperty("currencies")[0];
        Assert.Equal(0m, empty.GetProperty("spending").GetDecimal());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("changePercent").ValueKind);
        var arbitrary = await Read(alice, "/api/v1/reports/summary?dateFrom=2026-09-05&dateTo=2026-09-08&currency=USD");
        Assert.Equal("2026-09-02", arbitrary.GetProperty("previousDateFrom").GetString());
        using var unseeded = await Client(factory, "new-user");
        Assert.Empty((await Read(unseeded, "/api/v1/accounts")).GetProperty("items").EnumerateArray());
        Assert.Empty((await Read(unseeded, "/api/v1/reports/summary")).GetProperty("currencies").EnumerateArray());
        var emptyWorkspace = await Read(unseeded, "/api/v1/workspace");
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM"), emptyWorkspace.GetProperty("defaultMonth").GetString());
        Assert.Equal(JsonValueKind.Null, emptyWorkspace.GetProperty("asOf").ValueKind);
        Assert.Empty(emptyWorkspace.GetProperty("currencies").EnumerateArray());
        Assert.False(emptyWorkspace.GetProperty("seeded").GetBoolean());

        var editable = all.EnumerateArray().First(t => t.GetProperty("classification").GetString() == "expense" &&
            t.GetProperty("amount").GetDecimal() < 0 && !t.GetProperty("pending").GetBoolean());
        var id = editable.GetProperty("id").GetString();
        var providerCategory = editable.GetProperty("providerCategoryId").GetString();
        var category = providerCategory == "shopping" ? "health" : "shopping";
        var patch = $"/api/v1/transactions/{id}/category";
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PatchAsync(patch,
            new StringContent("{", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PatchAsJsonAsync(patch, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PatchAsJsonAsync(patch, new { categoryId = "unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PatchAsJsonAsync(patch, new { categoryId = category })).StatusCode);
        var changed = await alice.PatchAsJsonAsync(patch, new { categoryId = category });
        changed.EnsureSuccessStatusCode();
        Assert.Equal(providerCategory, (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("providerCategoryId").GetString());
        var deletedAccount = await db.Accounts.SingleAsync(a => a.OwnerId == "alice" && a.DeletedAt != null);
        await DevelopmentSeed.Apply(db, ["alice", "bob"]);
        Assert.Equal(initialCount, await db.Transactions.CountAsync());
        Assert.NotNull((await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == deletedAccount.Id)).DeletedAt);
        using (var restarted = Factory(connection))
        using (var restartedAlice = await Client(restarted, "alice"))
        {
            var filtered = await Read(restartedAlice, $"/api/v1/transactions?month=2026-09&categoryId={category}&pageSize=100");
            Assert.Contains(filtered.GetProperty("items").EnumerateArray(), t => t.GetProperty("id").GetString() == id && t.GetProperty("manualCategoryId").GetString() == category);
            var categoryReport = (await Read(restartedAlice, $"/api/v1/reports/summary?month=2026-09&categoryId={category}&currency=USD")).GetProperty("currencies")[0];
            var categoryRows = filtered.GetProperty("items").EnumerateArray().Where(t => t.GetProperty("classification").GetString() == "expense" && !t.GetProperty("pending").GetBoolean());
            Assert.Equal(-categoryRows.Sum(t => t.GetProperty("amount").GetDecimal()), categoryReport.GetProperty("spending").GetDecimal());
            var cleared = await restartedAlice.PatchAsJsonAsync(patch, new { categoryId = (string?)null });
            cleared.EnsureSuccessStatusCode();
            Assert.Equal(providerCategory, (await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("categoryId").GetString());
        }

        // A different owner's dates/currency must drive defaults, independently of the development fixture.
        var otherConnection = new FinancialConnection { Id = Guid.NewGuid(), OwnerId = "new-user", Provider = "imported", ProviderItemId = "other-item" };
        var otherAccount = new Account { Id = Guid.NewGuid(), OwnerId = "new-user", ConnectionId = otherConnection.Id,
            ProviderAccountId = "other-account", Currency = "CAD" };
        db.Connections.Add(otherConnection);
        db.Accounts.Add(otherAccount);
        foreach (var date in new[] { new DateOnly(2023, 11, 4), new DateOnly(2024, 2, 29) })
            db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), OwnerId = "new-user", AccountId = otherAccount.Id,
                ProviderTransactionId = date.ToString("yyyy-MM-dd"), Date = date, Currency = "CAD", Amount = -42.50m });
        db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), OwnerId = "new-user", AccountId = otherAccount.Id,
            ProviderTransactionId = "removed-future", Date = new DateOnly(2028, 1, 1), Currency = "EUR", RemovedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var otherWorkspace = await Read(unseeded, "/api/v1/workspace");
        Assert.Equal("2024-02", otherWorkspace.GetProperty("defaultMonth").GetString());
        Assert.Equal("CAD", otherWorkspace.GetProperty("defaultCurrency").GetString());
        Assert.Equal("2024-02-29", otherWorkspace.GetProperty("asOf").GetString());
        Assert.Equal(new[] { "CAD" }, otherWorkspace.GetProperty("currencies").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(new[] { DateTime.UtcNow.ToString("yyyy-MM"), "2024-02", "2023-11" },
            otherWorkspace.GetProperty("months").EnumerateArray().Select(m => m.GetString()));
        Assert.False(otherWorkspace.GetProperty("seeded").GetBoolean());

        using var connector = await Client(factory, "connector");
        Assert.Equal(3, (await Read(connector, "/api/v1/workspace")).GetProperty("connectableInstitutions").GetArrayLength());
        foreach (var institution in new string?[] { null, "", "unknown" })
            Assert.Equal(HttpStatusCode.BadRequest, (await connector.PostAsJsonAsync("/api/v1/connections", new { institutionId = institution })).StatusCode);
        var connected = await connector.PostAsJsonAsync("/api/v1/connections", new { institutionId = "capital-one", ownerId = "bob" });
        connected.EnsureSuccessStatusCode();
        var connectionId = (await connected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("connectionId").GetGuid();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await connector.PostAsJsonAsync($"/api/v1/connections/{connectionId}/sync", new { })).StatusCode);
        Assert.False(await db.SyncRuns.AnyAsync());
        var connectedAccounts = (await Read(connector, "/api/v1/accounts")).GetProperty("items");
        Assert.Single(connectedAccounts.EnumerateArray());
        Assert.Equal("360 checking", connectedAccounts[0].GetProperty("name").GetString());
        Assert.Equal(3, (await Read(bob, "/api/v1/accounts")).GetProperty("items").GetArrayLength());
        var imported = await Read(connector, "/api/v1/transactions?pageSize=100");
        Assert.True(imported.GetProperty("totalCount").GetInt32() > 0);
        var importedId = imported.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await connector.PatchAsJsonAsync($"/api/v1/transactions/{importedId}/category", new { categoryId = "health" })).EnsureSuccessStatusCode();
        await db.Connections.Where(c => c.Id == connectionId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "disconnected"));
        (await connector.PostAsJsonAsync("/api/v1/connections", new { institutionId = "capital-one" })).EnsureSuccessStatusCode();
        Assert.Equal(imported.GetProperty("totalCount").GetInt32(), (await Read(connector, "/api/v1/transactions")).GetProperty("totalCount").GetInt32());
        Assert.Equal("connected", (await Read(connector, "/api/v1/accounts")).GetProperty("items")[0].GetProperty("connectionStatus").GetString());
        using (var restarted = Factory(connection))
        using (var restartedConnector = await Client(restarted, "connector"))
        {
            var rows = (await Read(restartedConnector, "/api/v1/transactions?pageSize=100")).GetProperty("items");
            Assert.Contains(rows.EnumerateArray(), row => row.GetProperty("id").GetGuid() == importedId && row.GetProperty("manualCategoryId").GetString() == "health");
        }
        await db.Transactions.Where(t => t.Id == importedId).ExecuteUpdateAsync(s => s.SetProperty(t => t.RemovedAt, DateTimeOffset.UtcNow));
        (await connector.PostAsJsonAsync("/api/v1/connections", new { institutionId = "capital-one" })).EnsureSuccessStatusCode();
        Assert.Equal(imported.GetProperty("totalCount").GetInt32() - 1, (await Read(connector, "/api/v1/transactions")).GetProperty("totalCount").GetInt32());

        // PostgreSQL, not just application filters, rejects cross-owner references.
        db.ChangeTracker.Clear();
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = "alice", ConnectionId =
            await db.Connections.Where(c => c.OwnerId == "bob").Select(c => c.Id).FirstAsync(), ProviderAccountId = "cross-owner" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE transactions RENAME TO unavailable_transactions");
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await alice.GetAsync("/health/ready")).StatusCode);
            var failure = await alice.GetAsync("/api/v1/transactions");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
            var detail = await failure.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SELECT", detail);
            Assert.DoesNotContain("Password", detail);
            Assert.DoesNotContain("unavailable_transactions", detail);
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE unavailable_transactions RENAME TO transactions"); }
        using var unavailableFactory = Factory("Host=127.0.0.1;Port=1;Database=doughtracker_tests_missing;Username=missing;Timeout=1");
        using var unavailableClient = await Client(unavailableFactory, "alice");
        Assert.Equal(HttpStatusCode.OK, (await unavailableClient.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await unavailableClient.GetAsync("/health/ready")).StatusCode);
    }
}
