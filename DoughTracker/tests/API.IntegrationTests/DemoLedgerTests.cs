using Application;
using Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

public class DemoLedgerTests
{
    [Fact]
    public async Task MockWorkspaceRegistrationIsSingletonAndEndpointsRequireAuthentication()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        var workspace = factory.Services.GetRequiredService<DemoWorkspace>();
        Assert.Same(workspace, factory.Services.GetRequiredService<DemoWorkspace>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/demo/workspace")).StatusCode);

        var response = await client.PostAsJsonAsync("/api/v1/dev/token", new { uid = "ledger-user" });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/demo/workspace")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/demo/connections",
            new { institutionId = "capital-one" })).StatusCode);
        Assert.Equal(4, workspace.Snapshot("2026-09").Accounts.Length);
    }

    [Fact]
    public void Provider_data_reports_and_connection_lifecycle_agree()
    {
        var workspace = new DemoWorkspace(PlaidDemoData.Seed());
        var before = workspace.Snapshot("2026-09");
        var posted = before.Transactions.Where(t => t.Classification == "expense" && !t.Pending).ToArray();
        Assert.Equal(-posted.Sum(t => t.Amount), before.Report.Spending);
        Assert.Equal(before.Report.Spending, before.Report.Categories.Sum(c => c.Amount));
        Assert.Equal(before.Report.Spending, before.Report.Daily.Last().Current);
        Assert.Equal(119.55m, before.Report.PendingSpending);
        Assert.Equal(39.90m, before.Report.Refunds);
        Assert.Contains(before.Transactions, t => t.Classification == "income" && t.Amount > 0);
        Assert.Contains(before.Transactions, t => t.Classification == "transfer");
        Assert.Contains(before.Transactions, t => t.Classification == "card-payment");
        Assert.Null(before.Accounts.Single(a => a.Subtype == "savings").AvailableBalance);

        var source = PlaidDemoData.Initial("amex").Added.First(t => t.Amount > 0);
        Assert.Equal(-source.Amount, PlaidDemoData.Normalize(source).Amount);
        Assert.True(workspace.SetCategory("demo-unknown", "shopping"));
        Assert.True(workspace.SetCategory("demo-pending-coffee", "dining"));
        var update = PlaidDemoData.Normalize(PlaidDemoData.Update("amex"));
        Assert.True(workspace.ApplyBatch("amex", update));
        var after = workspace.Snapshot("2026-09");
        Assert.DoesNotContain(after.Transactions, t => t.Id is "demo-pending-coffee" or "demo-reversed");
        Assert.Equal("shopping", after.Transactions.Single(t => t.Id == "demo-unknown").ManualCategoryId);
        Assert.Equal("dining", after.Transactions.Single(t => t.Id == "demo-posted-coffee").ManualCategoryId);
        Assert.Equal(before.Report.Spending + 21.50m - 49m, after.Report.Spending);
        Assert.Equal(101.05m, after.Report.PendingSpending);
        Assert.True(workspace.ApplyBatch("amex", update));
        Assert.Equal(after.Report.Spending, workspace.Snapshot("2026-09").Report.Spending);
        Assert.Equal(after.Transactions.Length, workspace.Snapshot("2026-09").Transactions.Length);

        Assert.True(workspace.Disconnect("amex"));
        Assert.Equal(after.Report.Spending, workspace.Snapshot("2026-09").Report.Spending);
        Assert.False(workspace.ApplyBatch("amex", update));
        Assert.True(workspace.Connect("amex"));
        Assert.Equal(after.Transactions.Length, workspace.Snapshot("2026-09").Transactions.Length);
        Assert.True(workspace.Connect("capital-one"));
        var connected = workspace.Snapshot("2026-09");
        Assert.Equal(4, connected.Accounts.Length);
        Assert.True(connected.Report.Spending > after.Report.Spending);
        Assert.True(workspace.Connect("capital-one"));
        Assert.Equal(connected.Transactions.Length, workspace.Snapshot("2026-09").Transactions.Length);
        Assert.Empty(workspace.Snapshot("2026-09", search: "not-a-real-merchant").Transactions);
        Assert.Null(workspace.Snapshot("2026-04").Report.ChangePercent);
        var extra = before.Transactions.First(t => t.Classification == "expense") with {
            Id = "month-end", Date = new DateOnly(2026, 8, 31), Pending = false
        };
        var monthEnd = new DemoWorkspace(PlaidDemoData.Seed() with {
            Transactions = [.. PlaidDemoData.Seed().Transactions, extra]
        }).Snapshot("2026-09");
        Assert.Equal(monthEnd.Report.PreviousSpending, monthEnd.Report.Daily.Last().Previous);
    }
}
