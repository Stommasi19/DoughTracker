using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application;
using Domain;
using Microsoft.Extensions.Configuration;

namespace Infrastructure;

public sealed class PlaidFailure(string code) : Exception("Bank request unavailable.")
{
    // Only explicitly allowed codes are exposed; never preserve provider messages/payloads.
    public string Code { get; } = code is "ITEM_LOGIN_REQUIRED" or "INVALID_ACCESS_TOKEN" or
        "ITEM_NOT_FOUND" or "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" or "RATE_LIMIT_EXCEEDED"
        ? code : "PROVIDER_UNAVAILABLE";
}

public sealed class PlaidClient(HttpClient http, IConfiguration config)
{
    private static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true
    };
    public bool Enabled => config.GetValue<bool>("Plaid:Enabled");

    public async Task<JsonElement> Call(string route, object fields, CancellationToken ct, bool retry = true)
    {
        if (!Enabled) throw new LedgerRequestException("Plaid Sandbox is not configured.", 503);
        var body = JsonSerializer.SerializeToElement(fields, Json).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value);
        body["client_id"] = config["Plaid:ClientId"];
        body["secret"] = config["Plaid:Secret"];
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await http.PostAsJsonAsync(route, body, Json, ct);
                var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                if (response.IsSuccessStatusCode) return payload;
                var code = payload.TryGetProperty("error_code", out var error) ? error.GetString() ?? "" : "";
                if (!retry || attempt >= 2 || (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500))
                    throw new PlaidFailure(code);
                var delay = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(attempt + 1);
                // Refuse an excessive provider delay instead of retrying before its rate-limit window.
                if (delay > TimeSpan.FromSeconds(30)) throw new PlaidFailure("RATE_LIMIT_EXCEEDED");
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), ct);
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !ct.IsCancellationRequested)
            {
                if (!retry || attempt >= 2) throw new PlaidFailure("PROVIDER_UNAVAILABLE");
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
            }
            catch (JsonException) { throw new PlaidFailure("PROVIDER_UNAVAILABLE"); }
        }
    }

    public async Task<LinkToken> Link(string owner, string? accessToken, CancellationToken ct)
    {
        var fields = new Dictionary<string, object?> {
            ["user"] = new { client_user_id = owner }, ["client_name"] = "DoughTracker",
            ["country_codes"] = new[] { "US" }, ["language"] = "en"
        };
        if (accessToken is null)
        {
            fields["products"] = new[] { "transactions" };
            fields["transactions"] = new { days_requested = 180 };
            fields["account_filters"] = new {
                depository = new { account_subtypes = new[] { "checking", "savings" } },
                credit = new { account_subtypes = new[] { "credit card" } }
            };
        }
        else fields["access_token"] = accessToken;
        if (!string.IsNullOrWhiteSpace(config["Plaid:WebhookUrl"])) fields["webhook"] = config["Plaid:WebhookUrl"];
        if (!string.IsNullOrWhiteSpace(config["Plaid:RedirectUri"])) fields["redirect_uri"] = config["Plaid:RedirectUri"];
        var response = await Call("link/token/create", fields, ct, retry: false);
        return new(response.GetProperty("link_token").GetString()!, response.GetProperty("expiration").GetDateTimeOffset());
    }

    public async Task<SyncBatch> Fetch(string accessToken, string? savedCursor, CancellationToken ct)
    {
        // A pagination mutation invalidates every page in that fetch, not only the last page.
        for (var restart = 0; ; restart++)
        {
            var added = new List<LedgerTransaction>(); var modified = new List<LedgerTransaction>();
            var removed = new List<string>(); var accounts = new Dictionary<string, DemoAccount>();
            var cursor = savedCursor;
            try
            {
                var metadata = await Call("accounts/get", new { access_token = accessToken }, ct);
                MapAccounts(metadata.GetProperty("accounts").Deserialize<PlaidAccount[]>(Json)!, accounts);
                for (var page = 0; ; page++)
                {
                    if (page >= 1000) throw new PlaidFailure("PROVIDER_UNAVAILABLE");
                    var response = await Call("transactions/sync", new {
                        access_token = accessToken, cursor, count = 500
                    }, ct);
                    MapAccounts(response.GetProperty("accounts").Deserialize<PlaidAccount[]>(Json)!, accounts);
                    foreach (var (name, target) in new[] { ("added", added), ("modified", modified) })
                        foreach (var row in response.GetProperty(name).Deserialize<PlaidTransaction[]>(Json)!)
                        {
                            if (!accounts.ContainsKey(row.AccountId)) continue;
                            target.Add(PlaidDemoData.Normalize(row) with { Currency = Currency(row.IsoCurrencyCode) });
                        }
                    removed.AddRange(response.GetProperty("removed").Deserialize<PlaidRemoved[]>(Json)!.Select(r => r.TransactionId));
                    var next = response.GetProperty("next_cursor").GetString()!;
                    if (!response.GetProperty("has_more").GetBoolean()) return new(accounts.Values.ToArray(), new(added.ToArray(), modified.ToArray(), removed.ToArray()), next);
                    if (next == cursor) throw new PlaidFailure("PROVIDER_UNAVAILABLE");
                    cursor = next;
                }
            }
            catch (PlaidFailure error) when (error.Code == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && restart < 2) { }
        }
    }

    private static void MapAccounts(PlaidAccount[] sources, Dictionary<string, DemoAccount> accounts)
    {
        foreach (var account in sources)
        {
            if (account.Type != "credit" && !(account.Type == "depository" && account.Subtype is "checking" or "savings")) continue;
            accounts[account.AccountId] = new(account.AccountId, "", account.Name, account.Mask,
                account.Type, account.Subtype, account.Balances.Current, account.Balances.Available,
                Currency(account.Balances.IsoCurrencyCode));
        }
    }

    private static string Currency(string? currency) => currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z')
        ? currency : throw new PlaidFailure("UNSUPPORTED_CURRENCY");
}
