using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Application;
using API;
using Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
if (builder.Environment.IsDevelopment())
    builder.Services.AddMockInfrastructure();

var app = builder.Build();
app.Services.InitializeInfrastructure();
await app.Services.InitializeLedger(builder.Configuration);
app.UseExceptionHandler(new ExceptionHandlerOptions {
    SuppressDiagnosticsCallback = _ => true,
    ExceptionHandler = async context => {
        var exception = context.Features.Get<IExceptionHandlerFeature>()!.Error;
        var requestError = exception as LedgerRequestException;
        var badRequest = exception as BadHttpRequestException;
        var storage = exception is NpgsqlException or DbUpdateException or TimeoutException;
        var status = requestError?.Status ?? badRequest?.StatusCode ?? (storage ? 503 : 500);
        if (requestError is null && badRequest is null)
            app.Logger.LogError("Request failed: {ErrorType}, trace {TraceId}", exception.GetType().Name, context.TraceIdentifier);
        await Results.Problem(statusCode: status, title: status == 404 ? "Resource unavailable" :
            status == 400 ? "Invalid request" : "Request unavailable",
            detail: requestError?.Message ?? (badRequest is not null ? "Provide a valid request body and query parameters." :
                "The ledger could not complete this request. Try again shortly."))
            .ExecuteAsync(context);
    }
});
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    var demo = app.MapGroup("/api/demo").RequireAuthorization();
    demo.MapGet("/workspace", (DemoWorkspace workspace, string? month, string? accountId,
        string? categoryId, string? search) => {
        month ??= workspace.Seed.AsOf.ToString("yyyy-MM");
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _) || !Enumerable.Range(0, 6).Any(n =>
                workspace.Seed.AsOf.AddMonths(-n).ToString("yyyy-MM") == month))
            return Results.BadRequest(new { error = "Choose one of the six demo months." });
        if (!string.IsNullOrEmpty(accountId) && !workspace.Seed.Accounts.Any(a => a.Id == accountId))
            return Results.BadRequest(new { error = "Account not found." });
        if (!string.IsNullOrEmpty(categoryId) && !DemoWorkspace.Categories.Any(c => c.Id == categoryId))
            return Results.BadRequest(new { error = "Category not found." });
        if (search?.Length > 120) return Results.BadRequest(new { error = "Search must be 120 characters or fewer." });
        return Results.Ok(workspace.Snapshot(month, accountId, categoryId, search));
    });
    demo.MapPost("/connections", (ConnectRequest request, DemoWorkspace workspace) =>
        workspace.Connect(request.InstitutionId) ? Results.Ok() :
            Results.BadRequest(new { error = "Choose an available demo institution." }));
    demo.MapPost("/connections/{id}/disconnect", (string id, DemoWorkspace workspace) =>
        workspace.Disconnect(id) ? Results.Ok() : Results.NotFound(new { error = "Connection not found." }));
    demo.MapPost("/connections/{id}/sync", (string id, DemoWorkspace workspace) => {
        if (!PlaidDemoData.Institutions.Any(i => i.Id == id)) return Results.NotFound();
        return workspace.ApplyBatch(id, PlaidDemoData.Normalize(PlaidDemoData.Update(id))) ? Results.Ok() :
            Results.Conflict(new { error = "Connect this institution before syncing." });
    });
    demo.MapPatch("/transactions/{id}/category", (string id, CategoryRequest request, DemoWorkspace workspace) => {
        if (request.CategoryId is not null && !DemoWorkspace.Categories.Any(c => c.Id == request.CategoryId))
            return Results.BadRequest(new { error = "Choose a valid category." });
        return workspace.SetCategory(id, request.CategoryId) ? Results.Ok() : Results.NotFound();
    });
    demo.MapGet("/provider/{id}", (string id, bool? update) => {
        if (!PlaidDemoData.Institutions.Any(i => i.Id == id)) return Results.NotFound();
        return Results.Json(update == true ? PlaidDemoData.Update(id) : PlaidDemoData.Initial(id),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapLedger();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
{
    uid = user.FindFirstValue(ClaimTypes.NameIdentifier),
}));

if (builder.Configuration.GetValue<bool>("Firebase:UseMockAuthentication"))
{
    api.MapPost("/dev/token", (MockTokenRequest request, ITimeLimitedDataProtector tokens, HttpResponse response) =>
    {
        if (string.IsNullOrWhiteSpace(request.Uid) || request.Uid.Length > 128)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["uid"] = ["Provide a non-empty UID of at most 128 characters."],
            });
        }

        response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            idToken = tokens.Protect(request.Uid, TimeSpan.FromHours(1)),
            tokenType = "Bearer",
            expiresIn = 3600,
        });
    }).AllowAnonymous();
}

app.Run();

public partial class Program;
public record ConnectRequest(string InstitutionId);
public record CategoryRequest(string? CategoryId);
public sealed record MockTokenRequest(string? Uid);
