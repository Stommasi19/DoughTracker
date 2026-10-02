using Application;
using Domain;

namespace Infrastructure;

// Provider-shaped fixtures are kept here; reports and the UI only see the normalized ledger.
public record PlaidCategory(string Primary, string Detailed, string? ConfidenceLevel, string Version = "v2");
public record PlaidBalances(decimal? Available, decimal? Current, decimal? Limit,
    string? IsoCurrencyCode = "USD", string? UnofficialCurrencyCode = null);
public record PlaidAccount(string AccountId, PlaidBalances Balances, string? Mask, string Name,
    string? OfficialName, string Type, string Subtype);
public record PlaidTransaction(string TransactionId, string AccountId, decimal Amount, DateOnly Date,
    DateOnly? AuthorizedDate, string Name, string? MerchantName, bool Pending,
    string? PendingTransactionId, PlaidCategory? PersonalFinanceCategory, string PaymentChannel,
    string? IsoCurrencyCode = "USD", string? UnofficialCurrencyCode = null,
    string? LogoUrl = null, string? Website = null);
public record PlaidRemoved(string TransactionId, string AccountId);
public record PlaidSyncResponse(PlaidAccount[] Accounts, PlaidTransaction[] Added,
    PlaidTransaction[] Modified, PlaidRemoved[] Removed, string NextCursor,
    bool HasMore = false, string TransactionsUpdateStatus = "HISTORICAL_UPDATE_COMPLETE",
    string RequestId = "demo-request");

public static class PlaidDemoData
{
    public static readonly DateOnly AsOf = new(2026, 9, 30);
    public static readonly DemoInstitution[] Institutions = [
        new("chase", "Chase", "C", true), new("amex", "American Express", "AE", true),
        new("capital-one", "Capital One", "CO", false)
    ];
    private static readonly Dictionary<string, PlaidAccount[]> Accounts = new() {
        ["chase"] = [
            new("demo-checking", new(6284.32m, 6403.87m, null), "4821", "Everyday checking", "Total Checking", "depository", "checking"),
            new("demo-savings", new(null, 12450m, null), "9036", "Rainy day savings", "Savings", "depository", "savings")
        ],
        ["amex"] = [new("demo-credit", new(8156.74m, 1843.26m, 10000m), "1008", "Blue Cash Everyday", null, "credit", "credit card")],
        ["capital-one"] = [new("demo-capital", new(2778.41m, 2831.20m, null), "7624", "360 checking", "360 Checking", "depository", "checking")]
    };

    public static PlaidSyncResponse Initial(string institutionId)
    {
        var transactions = new List<PlaidTransaction>();
        void Add(string id, string account, decimal amount, DateOnly date, string? merchant,
            string primary, string detailed, string channel = "online", bool pending = false)
        {
            transactions.Add(new(id, account, amount, date, date.AddDays(-1),
                merchant is null ? "POS PURCHASE 0930 LOCAL MARKET" : merchant.ToUpperInvariant() + " PURCHASE",
                merchant, pending, null, primary == "" ? null : new(primary, detailed, "VERY_HIGH"), channel));
        }

        for (var n = 0; n < 6; n++)
        {
            var month = new DateOnly(AsOf.Year, AsOf.Month, 1).AddMonths(-n);
            var tag = month.ToString("yyyy-MM");
            var factor = n == 1 ? 1.19m : 0.96m + n * 0.015m;
            decimal Price(decimal amount) => decimal.Round(amount * factor, 2);
            if (institutionId == "chase")
            {
                Add(tag + "-rent", "demo-checking", 1850m, month, "Oak & Elm Properties", "RENT_AND_UTILITIES", "RENT_AND_UTILITIES_RENT");
                Add(tag + "-internet", "demo-checking", 65m, month.AddDays(4), "Sonic", "RENT_AND_UTILITIES", "RENT_AND_UTILITIES_INTERNET_AND_CABLE");
                Add(tag + "-electric", "demo-checking", Price(82.41m), month.AddDays(12), "PG&E", "RENT_AND_UTILITIES", "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY");
                Add(tag + "-payroll", "demo-checking", -5200m, month.AddDays(14), null, "INCOME", "INCOME_SALARY", "other");
                Add(tag + "-save-out", "demo-checking", 500m, month.AddDays(15), "Savings transfer", "TRANSFER_OUT", "TRANSFER_OUT_SAVINGS", "other");
                Add(tag + "-save-in", "demo-savings", -500m, month.AddDays(15), "Savings transfer", "TRANSFER_IN", "TRANSFER_IN_SAVINGS", "other");
                Add(tag + "-payment-out", "demo-checking", 1270m, month.AddDays(19), "American Express payment", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", "other");
                Add(tag + "-transit", "demo-checking", 60m, month.AddDays(8), "Clipper", "TRANSPORTATION", "TRANSPORTATION_PUBLIC_TRANSIT");
            }
            if (institutionId == "amex")
            {
                for (var week = 0; week < 4; week++)
                {
                    Add(tag + "-groceries-" + week, "demo-credit", Price(74.23m + week * 5.37m), month.AddDays(3 + week * 7),
                        week % 2 == 0 ? "Trader Joe’s" : "Whole Foods Market", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES", "in store");
                    Add(tag + "-dinner-" + week, "demo-credit", Price(38.75m + week * 6.28m), month.AddDays(5 + week * 7),
                        week % 2 == 0 ? "Souvla" : "Tartine", "FOOD_AND_DRINK", "FOOD_AND_DRINK_RESTAURANT", "in store");
                    Add(tag + "-coffee-" + week, "demo-credit", 6.50m, month.AddDays(2 + week * 7), "Blue Bottle Coffee",
                        "FOOD_AND_DRINK", "FOOD_AND_DRINK_COFFEE", "in store");
                }
                Add(tag + "-target", "demo-credit", Price(98.46m), month.AddDays(10), "Target", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_SUPERSTORES", "in store");
                Add(tag + "-uniqlo", "demo-credit", Price(129.90m), month.AddDays(18), "Uniqlo", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_CLOTHING_AND_ACCESSORIES");
                Add(tag + "-refund", "demo-credit", -39.90m, month.AddDays(25), "Uniqlo", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_CLOTHING_AND_ACCESSORIES");
                Add(tag + "-gas", "demo-credit", Price(54.36m), month.AddDays(16), "Shell", "TRANSPORTATION", "TRANSPORTATION_GAS", "in store");
                Add(tag + "-spotify", "demo-credit", 11.99m, month.AddDays(6), "Spotify", "ENTERTAINMENT", "ENTERTAINMENT_MUSIC_AND_AUDIO");
                Add(tag + "-netflix", "demo-credit", 15.49m, month.AddDays(11), "Netflix", "ENTERTAINMENT", "ENTERTAINMENT_TV_AND_MOVIES");
                Add(tag + "-health", "demo-credit", 25m, month.AddDays(21), "One Medical", "MEDICAL", "MEDICAL_PRIMARY_CARE");
                Add(tag + "-payment-in", "demo-credit", -1270m, month.AddDays(19), "Payment received", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", "other");
            }
            if (institutionId == "capital-one")
            {
                Add(tag + "-capital-grocery", "demo-capital", Price(52.79m), month.AddDays(9), "Safeway", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES", "in store");
                Add(tag + "-capital-dinner", "demo-capital", Price(32.40m), month.AddDays(23), "Sweetgreen", "FOOD_AND_DRINK", "FOOD_AND_DRINK_RESTAURANT", "in store");
            }
        }
        if (institutionId == "amex")
        {
            Add("demo-pending-coffee", "demo-credit", 18.50m, AsOf, "Blue Bottle Coffee", "FOOD_AND_DRINK", "FOOD_AND_DRINK_COFFEE", "in store", true);
            Add("demo-pending-grocery", "demo-credit", 101.05m, AsOf, "Whole Foods Market", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES", "in store", true);
            Add("demo-unknown", "demo-credit", 24.60m, AsOf.AddDays(-1), null, "", "", "in store");
            Add("demo-reversed", "demo-credit", 49m, AsOf.AddDays(-2), "Target", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_SUPERSTORES");
        }
        // Nullable merchant names and authorization dates are real provider cases.
        transactions = transactions.Select(t => t.PersonalFinanceCategory?.Primary == "INCOME"
            ? t with { Name = "ACH CREDIT ACME PAYROLL", AuthorizedDate = null } : t).ToList();
        return new(Accounts[institutionId], transactions.ToArray(), [], [], "demo-initial-" + institutionId);
    }

    public static PlaidSyncResponse Update(string institutionId)
    {
        if (institutionId != "amex") return new(Accounts[institutionId], [], [], [], "demo-update-" + institutionId);
        var initial = Initial(institutionId);
        var posted = initial.Added.First(t => t.TransactionId == "demo-pending-coffee") with {
            TransactionId = "demo-posted-coffee", Pending = false,
            PendingTransactionId = "demo-pending-coffee", Amount = 21.50m
        };
        var modified = initial.Added.First(t => t.TransactionId == "demo-unknown") with {
            MerchantName = "Local Market", PersonalFinanceCategory = new("FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES", "HIGH")
        };
        return new(Accounts[institutionId], [posted], [modified],
            [new("demo-pending-coffee", "demo-credit"), new("demo-reversed", "demo-credit")], "demo-update-amex");
    }

    public static DemoSeed Seed() => new(AsOf, Institutions,
        Institutions.SelectMany(i => Accounts[i.Id].Select(a => new DemoAccount(a.AccountId, i.Id,
            a.Name, a.Mask, a.Type, a.Subtype, a.Balances.Current, a.Balances.Available,
            a.Balances.IsoCurrencyCode ?? "USD"))).ToArray(),
        Institutions.SelectMany(i => Initial(i.Id).Added.Select(Normalize)).ToArray());

    public static LedgerBatch Normalize(PlaidSyncResponse response) => new(
        response.Added.Select(Normalize).ToArray(), response.Modified.Select(Normalize).ToArray(),
        response.Removed.Select(t => t.TransactionId).ToArray());

    public static LedgerTransaction Normalize(PlaidTransaction t)
    {
        var primary = t.PersonalFinanceCategory?.Primary;
        var detailed = t.PersonalFinanceCategory?.Detailed;
        var classification = detailed == "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT" ? "card-payment" :
            primary is "TRANSFER_IN" or "TRANSFER_OUT" ? "transfer" : primary == "INCOME" ? "income" : "expense";
        var category = detailed switch {
            "FOOD_AND_DRINK_GROCERIES" => "groceries",
            "RENT_AND_UTILITIES_RENT" => "housing",
            _ => primary switch {
                "FOOD_AND_DRINK" => "dining", "GENERAL_MERCHANDISE" => "shopping",
                "TRANSPORTATION" => "transport", "ENTERTAINMENT" => "entertainment",
                "RENT_AND_UTILITIES" => "utilities", "MEDICAL" => "health", _ => "uncategorized"
            }
        };
        return new(t.TransactionId, t.AccountId, -t.Amount, t.IsoCurrencyCode ?? "USD", t.Date,
            t.AuthorizedDate, t.Name, t.MerchantName, category, detailed,
            t.PersonalFinanceCategory?.ConfidenceLevel, classification, t.Pending,
            t.PendingTransactionId, t.PaymentChannel);
    }
}
