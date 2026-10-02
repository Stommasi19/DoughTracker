namespace Domain;

public record DemoCategory(string Id, string Name);
public record DemoInstitution(string Id, string Name, string Initials, bool InitiallyConnected);
public record DemoAccount(string Id, string InstitutionId, string Name, string? Mask,
    string Type, string Subtype, decimal? CurrentBalance, decimal? AvailableBalance, string Currency);
public record LedgerTransaction(string Id, string AccountId, decimal Amount, string Currency,
    DateOnly Date, DateOnly? AuthorizedDate, string Description, string? MerchantName,
    string ProviderCategoryId, string? ProviderCategoryKey, string? CategoryConfidence,
    string Classification, bool Pending, string? PendingTransactionId, string PaymentChannel,
    string? ManualCategoryId = null, bool Removed = false)
{
    public string CategoryId => ManualCategoryId ?? ProviderCategoryId;
    public string Merchant => MerchantName ?? Description;
}

public record DemoSeed(DateOnly AsOf, DemoInstitution[] Institutions, DemoAccount[] Accounts,
    LedgerTransaction[] Transactions);
public record LedgerBatch(LedgerTransaction[] Added, LedgerTransaction[] Modified, string[] Removed);
public record DemoConnection(string InstitutionId, string Status, DateTimeOffset LastSyncAt);
public record SpendingGroup(string Id, string Name, decimal Amount, int Count);
public record DailySpending(int Day, decimal Current, decimal Previous);
public record MonthSpending(string Month, decimal Amount);
public record ExpenseReport(decimal Spending, decimal PreviousSpending, decimal? ChangePercent,
    decimal PendingSpending, decimal Refunds, decimal DailyAverage, int ExpenseCount,
    SpendingGroup[] Categories, SpendingGroup[] Merchants, DailySpending[] Daily,
    MonthSpending[] Trend);
public record DemoSnapshot(DateOnly AsOf, string Month, string[] Months, DemoCategory[] Categories,
    DemoInstitution[] Institutions, DemoConnection[] Connections, DemoAccount[] Accounts,
    LedgerTransaction[] Transactions, ExpenseReport Report);
