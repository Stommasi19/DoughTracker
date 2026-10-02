using System.Security.Claims;
using System.Text.Json;
using Application;
using Infrastructure;
using Domain;

namespace API;

public static class LedgerEndpoints
{
    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public static void MapLedger(this WebApplication app)
    {
        app.MapGet("/health/ready", async (LedgerDbContext db, CancellationToken ct) =>
            await LedgerInfrastructure.Ready(db, ct) ? Results.Ok(new { status = "ready" }) :
                Results.Problem(statusCode: 503, title: "Storage unavailable", detail: "The ledger is not ready. Try again shortly."))
            .AllowAnonymous().WithTags("Health").Produces(200).ProducesProblem(503);
        var api = app.MapGroup("/api/v1").RequireAuthorization().WithTags("Ledger");
        api.MapGet("/workspace", async (LedgerQueries ledger, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok((await ledger.Workspace(Owner(user), ct)) with {
                ConnectableInstitutions = app.Environment.IsDevelopment() && !app.Configuration.GetValue<bool>("Plaid:Enabled") ? PlaidDemoData.Institutions : [],
                PlaidEnabled = app.Configuration.GetValue<bool>("Plaid:Enabled"),
                SyncEnabled = app.Configuration.GetValue<bool>("Messaging:Enabled")
            }))
            .Produces<WorkspaceMetadata>().ProducesProblem(503)
            .WithDescription("Defaults and available periods/currencies from the owner's active ledger. Default month is the latest transaction month, or current UTC month for empty history. USD is preferred when present; otherwise the first stored currency, with USD for an empty workspace. AsOf is the latest visible transaction date, null for empty history. Periods include the current UTC month.");
        api.MapGet("/accounts", async (LedgerQueries ledger, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(new ItemsResult<AccountDto>(await ledger.Accounts(Owner(user), ct))))
            .Produces<ItemsResult<AccountDto>>().ProducesProblem(503);
        api.MapGet("/categories", async (LedgerQueries ledger, CancellationToken ct) =>
            Results.Ok(new ItemsResult<DemoCategory>(await ledger.Categories(ct))))
            .Produces<ItemsResult<DemoCategory>>().ProducesProblem(503);
        if (app.Environment.IsDevelopment())
            api.MapPost("/connections", async (ConnectRequest request, LedgerDbContext db, ClaimsPrincipal user, CancellationToken ct) => {
                var id = await DevelopmentSeed.Connect(db, Owner(user), request.InstitutionId, ct);
                return Results.Ok(new { connectionId = id });
            }).Produces(200).ProducesProblem(400).ProducesProblem(503)
                .WithDescription("Development only. Connect a mock institution and persist its sample accounts/history for the authenticated owner. Repeating a connection preserves existing rows and overrides. No real bank credentials or network calls.");
        api.MapGet("/transactions", async (LedgerQueries ledger, ClaimsPrincipal user, CancellationToken ct,
            string? month, string? dateFrom, string? dateTo, string? accountId, string? categoryId,
            string? currency, string? search, int page = 1, int pageSize = 20) => {
            var filter = LedgerFilter.Parse(month, dateFrom, dateTo, accountId, categoryId, currency, search, page, pageSize);
            return Results.Ok(await ledger.Transactions(Owner(user), filter, ct));
        }).Produces<TransactionPage>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(503)
            .WithDescription("Owned active transactions. month=YYYY-MM or inclusive dateFrom/exclusive dateTo. Literal merchant/description search (120 characters max). Ordered date descending then ID ascending; pageSize 1–100, default 20. Offset pages can shift during concurrent changes.");
        api.MapPatch("/transactions/{id:guid}/category", async (Guid id, JsonElement body,
            LedgerQueries ledger, ClaimsPrincipal user, CancellationToken ct) => {
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("categoryId", out var category) ||
                category.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new LedgerRequestException("Include categoryId as a valid category string or null to clear the override.");
            return Results.Ok(await ledger.SetCategory(Owner(user), id, category.GetString(), ct));
        }).Produces<LedgerTransaction>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(503)
            .WithDescription("Set a manual category; {\"categoryId\":null} restores the provider category. Financial classification is unchanged.");
        var reports = api.MapGroup("/reports").WithTags("Insights");
        reports.MapGet("/spending", async (LedgerQueries ledger, ClaimsPrincipal user, CancellationToken ct,
            string? month, string? dateFrom, string? dateTo, string? accountId, string? categoryId, string? currency) => {
            var result = await ledger.Report(Owner(user), LedgerFilter.Parse(month, dateFrom, dateTo, accountId, categoryId, currency, null, report: true), ct);
            return Results.Ok(new SpendingResult(result.DateFrom, result.DateTo, result.Currencies.Select(c =>
                new CurrencySpending(c.Currency, c.Report.Categories, c.Report.Merchants,
                    c.Report.Daily.Select(d => new DatedSpending(d.Day, result.DateFrom.AddDays(d.Day - 1), d.Current, d.Previous)).ToArray(),
                    c.Report.Trend)).ToArray()));
        }).Produces<SpendingResult>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(503)
            .WithDescription("Category/merchant groups, cumulative daily series, and six calendar months ending in dateFrom's month. Posted expenses net of refunds; separate currencies. Same filters as summary, no search/pagination.");
        reports.MapGet("/summary", async (LedgerQueries ledger, ClaimsPrincipal user, CancellationToken ct,
            string? month, string? dateFrom, string? dateTo, string? accountId, string? categoryId, string? currency) => {
            var filter = LedgerFilter.Parse(month, dateFrom, dateTo, accountId, categoryId, currency, null, report: true);
            var result = await ledger.Report(Owner(user), filter, ct);
            return Results.Ok(new SummaryResult(result.DateFrom, result.DateTo,
                filter.CalendarMonth ? result.DateFrom.AddMonths(-1) : result.DateFrom.AddDays(-(result.DateTo.DayNumber - result.DateFrom.DayNumber)),
                result.DateFrom, result.Currencies.Select(c => new CurrencySummary(c.Currency, c.Report.Spending,
                    c.Report.PreviousSpending, c.Report.ChangePercent, c.Report.PendingSpending, c.Report.Refunds,
                    c.Report.DailyAverage, c.Report.ExpenseCount)).ToArray()));
        }).Produces<SummaryResult>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(503)
            .WithDescription("Net posted spending and comparison. Inclusive dateFrom/exclusive dateTo, 1–366 days. Month compares the previous calendar month; explicit ranges compare the preceding equally long range. Excludes transfers, payments, income, removed/deleted/pending rows; pending spending is separate. Refunds use their own transaction date. Empty currencies return zero totals; no currency conversion.");
    }
}
