namespace Domain;

public class FinancialConnection
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderItemId { get; set; } = "";
    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string Status { get; set; } = "connected";
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? SecretReference { get; set; }
    public string? SyncCursor { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? ProviderRevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class SyncRun
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "requested";
    public int AttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}

// IDs in a normalized provider batch are provider keys, not internal UUIDs.
public record SyncBatch(DemoAccount[] Accounts, LedgerBatch Transactions, string NextCursor);

public class Account
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public Guid ConnectionId { get; set; }
    public FinancialConnection Connection { get; set; } = null!;
    public string ProviderAccountId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Mask { get; set; }
    public string Type { get; set; } = "";
    public string Subtype { get; set; } = "";
    public decimal? CurrentBalance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset? DeletedAt { get; set; }
}

public class Category
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class Transaction
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string ProviderTransactionId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateOnly Date { get; set; }
    public DateOnly? AuthorizedDate { get; set; }
    public string Description { get; set; } = "";
    public string? MerchantName { get; set; }
    public string ProviderCategoryId { get; set; } = "uncategorized";
    public string? ProviderCategoryKey { get; set; }
    public string? CategoryConfidence { get; set; }
    public string Classification { get; set; } = "expense";
    public bool Pending { get; set; }
    public string? PendingTransactionId { get; set; }
    public string PaymentChannel { get; set; } = "other";
    public string? ManualCategoryId { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}
