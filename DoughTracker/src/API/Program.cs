using System.Globalization;
using System.Text.Json;
using Application;
using Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton(new DemoWorkspace(PlaidDemoData.Seed()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    var demo = app.MapGroup("/api/demo");
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

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program;
public record ConnectRequest(string InstitutionId);
public record CategoryRequest(string? CategoryId);
