using Application;
using Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure;

public sealed class ConnectionStore(LedgerDbContext db, PlaidClient plaid, TokenSecrets secrets,
    IPublishEndpoint publish, IConfiguration config) : IConnectionStore
{
    public Task<ConnectionDto[]> List(string owner, CancellationToken ct) => db.Connections.AsNoTracking()
        .Where(c => c.OwnerId == owner).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
        .Select(c => new ConnectionDto(c.Id, c.InstitutionId, c.InstitutionName, c.Provider, c.Status,
            c.LastSyncAt, c.LastErrorCode, db.SyncRuns.Where(r => r.ConnectionId == c.Id)
                .OrderByDescending(r => r.Status == "requested" || r.Status == "processing")
                .ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Select(r => r.Status).FirstOrDefault()))
        .ToArrayAsync(ct);

    private async Task<FinancialConnection> Owned(string owner, Guid id, CancellationToken ct) =>
        await db.Connections.SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == owner, ct) ??
            throw new LedgerRequestException("Connection not found.", 404);

    public static Task Lock(LedgerDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);

    private Task RequestLock(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString() + ":requests"}, 0))", ct);

    private async Task<string> AccessToken(FinancialConnection connection, CancellationToken ct) =>
        connection.SecretReference is { } reference && await secrets.Read(reference, ct) is { } receipt
            ? receipt.AccessToken : throw new PlaidFailure("PROVIDER_UNAVAILABLE");

    public async Task<LinkToken> Link(string owner, Guid? connectionId, CancellationToken ct)
    {
        if (connectionId is null) return await plaid.Link(owner, null, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, connectionId.Value, ct);
        var connection = await Owned(owner, connectionId.Value, ct);
        if (connection.Provider != "plaid" || connection.Status is "disconnected" or "disconnecting")
            throw new LedgerRequestException("Connect this institution with a new Link session.", 409);
        return await plaid.Link(owner, await AccessToken(connection, ct), ct);
    }

    public async Task<ConnectionDto> Exchange(string owner, string publicToken, CancellationToken ct)
    {
        var reference = TokenSecrets.Reference(owner, publicToken);
        // Serialize replay of the same public token across processes, including partial failures.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, new Guid(Convert.FromHexString(reference).AsSpan(0, 16)), ct);
        var connection = await db.Connections.SingleOrDefaultAsync(c => c.OwnerId == owner && c.SecretReference == reference, ct);
        if (connection is null)
        {
            var receipt = await secrets.Read(reference, ct);
            if (receipt is null)
            {
                var exchanged = await plaid.Call("item/public_token/exchange", new { public_token = publicToken }, ct, retry: false);
                receipt = new(exchanged.GetProperty("access_token").GetString()!, exchanged.GetProperty("item_id").GetString()!);
                try { await secrets.Write(reference, receipt, CancellationToken.None); }
                catch
                {
                    // Best-effort compensation if durable secret storage failed after exchange.
                    try { await plaid.Call("item/remove", new { access_token = receipt.AccessToken }, CancellationToken.None); }
                    catch (PlaidFailure) { }
                    throw;
                }
            }
            var item = (await plaid.Call("item/get", new { access_token = receipt.AccessToken }, ct)).GetProperty("item");
            var institutionId = item.GetProperty("institution_id").GetString()!;
            var institution = (await plaid.Call("institutions/get_by_id", new {
                institution_id = institutionId, country_codes = new[] { "US" }
            }, ct)).GetProperty("institution");
            connection = new FinancialConnection { Id = Guid.NewGuid(), OwnerId = owner, Provider = "plaid",
                ProviderItemId = receipt.ItemId, InstitutionId = institutionId,
                InstitutionName = institution.GetProperty("name").GetString()!, SecretReference = reference,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Connections.Add(connection);
            await db.SaveChangesAsync(ct);
        }
        if (connection.Status is "disconnected" or "disconnecting")
            throw new LedgerRequestException("Create a new Link session for this institution.", 409);
        await RequestLock(connection.Id, ct);
        await RequestLocked(connection, "initial_link", ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await List(owner, ct)).Single(c => c.Id == connection.Id);
    }

    private async Task<Guid?> RequestLocked(FinancialConnection connection, string reason, CancellationToken ct)
    {
        if (connection.Status is "disconnected" or "disconnecting") return null;
        var active = await db.SyncRuns.Where(r => r.ConnectionId == connection.Id &&
            (r.Status == "requested" || r.Status == "processing")).Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);
        if (active is not null)
        {
            await db.SyncRuns.Where(r => r.Id == active).ExecuteUpdateAsync(s => s.SetProperty(r => r.RequestedAt, DateTimeOffset.UtcNow), ct);
            return active;
        }
        var run = new SyncRun { Id = Guid.NewGuid(), ConnectionId = connection.Id, Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow, RequestedAt = DateTimeOffset.UtcNow };
        db.SyncRuns.Add(run);
        await publish.Publish(new SyncConnectionRequested(connection.Id, run.Id), ct);
        return run.Id;
    }

    public async Task<Guid?> Request(string owner, Guid connectionId, string reason, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Messaging:Enabled"))
            throw new LedgerRequestException("Configure durable messaging before requesting synchronization.", 503);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RequestLock(connectionId, ct);
        var connection = await Owned(owner, connectionId, ct);
        if (connection.Status is "disconnected" or "disconnecting")
            throw new LedgerRequestException("This connection is disconnected.", 409);
        var run = await RequestLocked(connection, reason, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return run;
    }

    public async Task Reconcile(string? owner, bool startup, CancellationToken ct)
    {
        var ids = await db.Connections.AsNoTracking().Where(c => c.Status == "connected" &&
            c.Provider == "plaid" && (owner == null || c.OwnerId == owner))
            .Select(c => c.Id).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({id.ToString() + ":requests"}, 0)) AS \"Value\"")
                .SingleAsync(ct);
            if (!locked) continue;
            var connection = await db.Connections.SingleAsync(c => c.Id == id, ct);
            if (connection.Status != "connected" || !startup && connection.LastCheckedAt > DateTimeOffset.UtcNow.AddHours(-1)) continue;
            if (await db.SyncRuns.AnyAsync(r => r.ConnectionId == id && (r.Status == "requested" || r.Status == "processing"), ct)) continue;
            await RequestLocked(connection, startup ? "startup" : owner is null ? "hourly" : "stale_read", ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
    }

    public async Task Sync(SyncConnectionRequested command, IPublishEndpoint events, CancellationToken ct)
    {
        // ponytail: hold a per-connection database lock across provider calls; use a fenced lease if long fetches exhaust the pool.
        await Lock(db, command.ConnectionId, ct);
        var run = await db.SyncRuns.SingleOrDefaultAsync(r => r.Id == command.RunId && r.ConnectionId == command.ConnectionId, ct);
        if (run is null || run.Status is "completed" or "cancelled" or "failed") return;
        var connection = await db.Connections.SingleAsync(c => c.Id == command.ConnectionId, ct);
        if (connection.Status is "disconnected" or "disconnecting")
        {
            run.Status = "cancelled"; run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct); return;
        }
        var requestedAt = run.RequestedAt;
        run.AttemptCount++; run.Status = "processing";
        try
        {
            SyncBatch batch;
            if (connection.Provider == "development")
            {
                var response = connection.SyncCursor is null ? PlaidDemoData.Initial(connection.InstitutionId) : PlaidDemoData.Update(connection.InstitutionId);
                var accounts = response.Accounts.Select(a => new DemoAccount(a.AccountId, connection.InstitutionId,
                    a.Name, a.Mask, a.Type, a.Subtype, a.Balances.Current, a.Balances.Available, a.Balances.IsoCurrencyCode ?? "USD")).ToArray();
                batch = new(accounts, PlaidDemoData.Normalize(response), response.NextCursor);
            }
            else batch = await plaid.Fetch(await AccessToken(connection, ct), connection.SyncCursor, ct);
            await LedgerSync.Apply(db, connection, batch, ct);
            connection.LastSyncAt = DateTimeOffset.UtcNow; connection.LastErrorCode = null;
            connection.Status = "connected"; run.Status = "completed"; run.LastErrorCode = null;
            await events.Publish(new TransactionsChanged(connection.Id, run.Id), ct);
        }
        catch (PlaidFailure error)
        {
            run.Status = "failed"; run.LastErrorCode = error.Code; connection.LastErrorCode = error.Code;
            if (error.Code is "ITEM_LOGIN_REQUIRED" or "INVALID_ACCESS_TOKEN" or "ITEM_NOT_FOUND") connection.Status = "attention_required";
        }
        connection.LastCheckedAt = connection.UpdatedAt = DateTimeOffset.UtcNow;
        run.CompletedAt = DateTimeOffset.UtcNow;
        await RequestLock(connection.Id, ct);
        var latestRequest = await db.SyncRuns.Where(r => r.Id == run.Id).Select(r => r.RequestedAt).SingleAsync(ct);
        await db.SaveChangesAsync(ct);
        if (latestRequest > requestedAt)
        {
            await RequestLocked(connection, "follow_up", ct);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task Failed(SyncConnectionRequested command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, command.ConnectionId, ct);
        var run = await db.SyncRuns.SingleOrDefaultAsync(r => r.Id == command.RunId && r.ConnectionId == command.ConnectionId, ct);
        if (run is null || run.Status is "completed" or "cancelled") return;
        var connection = await db.Connections.SingleAsync(c => c.Id == command.ConnectionId, ct);
        run.Status = "failed"; run.LastErrorCode = "SYNC_FAILED"; run.AttemptCount++;
        run.CompletedAt = connection.LastCheckedAt = DateTimeOffset.UtcNow;
        connection.LastErrorCode = "SYNC_FAILED";
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public async Task Reconnected(string owner, Guid connectionId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, connectionId, ct);
        var connection = await Owned(owner, connectionId, ct);
        if (connection.Status is "disconnected" or "disconnecting") throw new LedgerRequestException("This connection is disconnected.", 409);
        // A browser callback only requests verification/sync; successful provider data clears attention_required.
        await RequestLock(connectionId, ct);
        await RequestLocked(connection, "reconnect", ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public async Task Disconnect(string owner, Guid connectionId, CancellationToken ct)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await Lock(db, connectionId, ct);
            var connection = await Owned(owner, connectionId, ct);
            if (connection.Status == "disconnected") return;
            connection.Status = "disconnecting"; connection.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SyncRuns.Where(r => r.ConnectionId == connectionId && (r.Status == "requested" || r.Status == "processing"))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "cancelled").SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow), ct);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        var failed = false;
        await using (var revoke = await db.Database.BeginTransactionAsync(ct))
        {
            await Lock(db, connectionId, ct);
            db.ChangeTracker.Clear();
            var connection = await Owned(owner, connectionId, ct);
            if (connection.Status == "disconnected") return;
            try
            {
                if (connection.Provider == "plaid" && connection.ProviderRevokedAt is null)
                {
                    try { await plaid.Call("item/remove", new { access_token = await AccessToken(connection, ct) }, ct); }
                    catch (PlaidFailure error) when (error.Code is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN") { }
                    connection.ProviderRevokedAt = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception error) when (error is PlaidFailure or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            { connection.LastErrorCode = "DISCONNECT_FAILED"; failed = true; }
            // Record revocation before deleting the secret so a later DB failure remains retryable.
            await db.SaveChangesAsync(ct); await revoke.CommitAsync(ct);
            if (failed) throw new LedgerRequestException("Disconnect could not finish. Retry to complete cleanup.", 503);
        }
        await using var cleanup = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, connectionId, ct);
        db.ChangeTracker.Clear();
        var owned = await Owned(owner, connectionId, ct);
        if (owned.Status == "disconnected") return;
        try
        {
            if (owned.SecretReference is { } reference) secrets.Delete(reference);
            owned.SecretReference = null; owned.Status = "disconnected"; owned.LastErrorCode = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            owned.LastErrorCode = "DISCONNECT_FAILED"; failed = true;
        }
        owned.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await cleanup.CommitAsync(ct);
        if (failed) throw new LedgerRequestException("Disconnect could not finish. Retry to complete cleanup.", 503);
    }

    public async Task DeleteAccount(string owner, Guid accountId, CancellationToken ct)
    {
        var connectionId = await db.Accounts.Where(a => a.OwnerId == owner && a.Id == accountId)
            .Select(a => (Guid?)a.ConnectionId).SingleOrDefaultAsync(ct) ?? throw new LedgerRequestException("Account not found.", 404);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, connectionId, ct);
        await db.Accounts.Where(a => a.OwnerId == owner && a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, DateTimeOffset.UtcNow), ct);
        await db.Transactions.Where(t => t.OwnerId == owner && t.AccountId == accountId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
}
