using System.Security.Claims;
using System.Text.Json;
using Application;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API;

public static class ConnectionEndpoints
{
    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public static void MapConnections(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/connections").RequireAuthorization().WithTags("Connections");
        api.MapPost("/link-token", async (Connections connections, ClaimsPrincipal user, HttpResponse response, CancellationToken ct) => {
            response.Headers.CacheControl = "no-store";
            var token = await connections.Link(Owner(user), null, ct);
            return Results.Ok(new LinkTokenResponse(token.LinkTokenValue, token.Expiration));
        }).Produces<LinkTokenResponse>().ProducesProblem(503)
            .WithDescription("Create a Plaid Sandbox Link token for this owner, requesting Transactions and 180 days of available history.");
        api.MapPost("/exchange-token", async (ExchangeTokenRequest request, Connections connections, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(await connections.Exchange(Owner(user), request.PublicToken, ct)))
            .Produces<ConnectionDto>().ProducesProblem(400).ProducesProblem(409).ProducesProblem(503)
            .WithDescription("Exchange the publicToken from Link onSuccess server-side, store its secret, and durably enqueue initial sync. Retry the same request to recover a persisted exchange receipt after a partial failure.");
        api.MapGet("/", async (Connections connections, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(new ItemsResult<ConnectionDto>(await connections.List(Owner(user), ct))))
            .Produces<ItemsResult<ConnectionDto>>().ProducesProblem(503);
        api.MapPost("/{id:guid}/reconnect", async (Guid id, Connections connections, ClaimsPrincipal user, HttpResponse response, CancellationToken ct) => {
            response.Headers.CacheControl = "no-store";
            var token = await connections.Link(Owner(user), id, ct);
            return Results.Ok(new LinkTokenResponse(token.LinkTokenValue, token.Expiration));
        }).Produces<LinkTokenResponse>().ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        api.MapPost("/{id:guid}/reconnect/complete", async (Guid id, Connections connections, ClaimsPrincipal user, CancellationToken ct) => {
            await connections.Reconnected(Owner(user), id, ct);
            return Results.Accepted();
        }).Produces(202).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503)
            .WithDescription("After update-mode onSuccess, request a provider check against the existing Item. No new public-token exchange; success clears attention_required only after provider verification.");
        api.MapPost("/{id:guid}/sync", async (Guid id, Connections connections, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Accepted(value: new { runId = await connections.Request(Owner(user), id, ct) }))
            .Produces(202).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        api.MapDelete("/{id:guid}", async (Guid id, Connections connections, ClaimsPrincipal user, CancellationToken ct) => {
            await connections.Disconnect(Owner(user), id, ct); return Results.NoContent();
        }).Produces(204).ProducesProblem(404).ProducesProblem(503)
            .WithDescription("Revoke Plaid and remove its stored token while retaining ledger history. Retry a disconnecting connection to complete failed cleanup.");
        app.MapDelete("/api/v1/accounts/{id:guid}", async (Guid id, Connections connections, ClaimsPrincipal user, CancellationToken ct) => {
            await connections.DeleteAccount(Owner(user), id, ct); return Results.NoContent();
        }).RequireAuthorization().WithTags("Ledger").Produces(204).ProducesProblem(404).ProducesProblem(503)
            .WithDescription("Tombstone one owned account and delete its transactions; future syncs ignore it. Sibling accounts stay linked.");
        app.MapPost("/webhooks/plaid", async (HttpRequest request, PlaidWebhookVerifier verifier,
            LedgerDbContext db, ConnectionStore store, CancellationToken ct) => {
            if (!request.Headers.TryGetValue("Plaid-Verification", out var header)) return Results.Unauthorized();
            var buffer = new byte[65537]; var length = 0;
            while (length < buffer.Length)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(length), ct);
                if (read == 0) break;
                length += read;
            }
            if (length > 65536) return Results.StatusCode(413);
            var body = buffer[..length];
            if (!await verifier.Verify(header.ToString(), body, ct)) return Results.Unauthorized();
            using var payload = JsonDocument.Parse(body);
            var root = payload.RootElement;
            if (!root.TryGetProperty("item_id", out var item) || item.ValueKind != JsonValueKind.String) return Results.BadRequest();
            var itemId = item.GetString();
            var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(c => c.Provider == "plaid" && c.ProviderItemId == itemId, ct);
            if (connection is null || connection.Status is "disconnected" or "disconnecting") return Results.Ok();
            if (root.TryGetProperty("webhook_type", out var type) && root.TryGetProperty("webhook_code", out var code) &&
                (type.GetString() == "TRANSACTIONS" && code.GetString() == "SYNC_UPDATES_AVAILABLE" ||
                    type.GetString() == "ITEM" && code.GetString() is "ERROR" or "LOGIN_REPAIRED"))
                await store.Request(connection.OwnerId, connection.Id, "webhook", ct);
            return Results.Ok();
        }).AllowAnonymous().WithTags("Connections").Produces(200).Produces(401).Produces(413).ProducesProblem(503)
            .WithDescription("Plaid ES256 signature, five-minute freshness and exact-body SHA-256 verification. Persists/coalesces work and returns without fetching financial data.");
    }
}

public record ExchangeTokenRequest(string? PublicToken);
public record LinkTokenResponse(string LinkToken, DateTimeOffset Expiration);
