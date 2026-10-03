namespace Application;

public record LinkToken(string LinkTokenValue, DateTimeOffset Expiration);
public record ConnectionDto(Guid Id, string InstitutionId, string InstitutionName, string Provider,
    string Status, DateTimeOffset? LastSyncAt, string? LastErrorCode, string? SyncStatus);
public record SyncConnectionRequested(Guid ConnectionId, Guid RunId);
public record TransactionsChanged(Guid ConnectionId, Guid RunId);

public interface IConnectionStore
{
    Task<LinkToken> Link(string owner, Guid? connectionId, CancellationToken ct);
    Task<ConnectionDto> Exchange(string owner, string publicToken, CancellationToken ct);
    Task<ConnectionDto[]> List(string owner, CancellationToken ct);
    Task<Guid?> Request(string owner, Guid connectionId, string reason, CancellationToken ct);
    Task Disconnect(string owner, Guid connectionId, CancellationToken ct);
    Task DeleteAccount(string owner, Guid accountId, CancellationToken ct);
    Task Reconnected(string owner, Guid connectionId, CancellationToken ct);
}

public sealed class Connections(IConnectionStore store)
{
    public Task<LinkToken> Link(string owner, Guid? id, CancellationToken ct) => store.Link(owner, id, ct);
    public Task<ConnectionDto[]> List(string owner, CancellationToken ct) => store.List(owner, ct);
    public Task<ConnectionDto> Exchange(string owner, string? publicToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicToken) || publicToken.Length > 1024)
            throw new LedgerRequestException("Provide a valid publicToken.");
        return store.Exchange(owner, publicToken, ct);
    }
    public Task<Guid?> Request(string owner, Guid id, CancellationToken ct) => store.Request(owner, id, "retry", ct);
    public Task Disconnect(string owner, Guid id, CancellationToken ct) => store.Disconnect(owner, id, ct);
    public Task DeleteAccount(string owner, Guid id, CancellationToken ct) => store.DeleteAccount(owner, id, ct);
    public Task Reconnected(string owner, Guid id, CancellationToken ct) => store.Reconnected(owner, id, ct);
}
