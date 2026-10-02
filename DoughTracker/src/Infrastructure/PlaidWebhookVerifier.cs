using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public sealed class PlaidWebhookVerifier(IServiceScopeFactory scopes, IMemoryCache cache)
{
    public async Task<bool> Verify(string jwt, byte[] body, CancellationToken ct)
    {
        try
        {
            if (jwt.Length > 8192) return false;
            var parts = jwt.Split('.');
            if (parts.Length != 3) return false;
            using var header = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "ES256") return false;
            var kid = header.RootElement.GetProperty("kid").GetString();
            if (string.IsNullOrWhiteSpace(kid) || kid.Length > 256) return false;
            if (!cache.TryGetValue("plaid-key:" + kid, out JsonElement key))
            {
                using var scope = scopes.CreateScope();
                key = (await scope.ServiceProvider.GetRequiredService<PlaidClient>().Call(
                    "webhook_verification_key/get", new { key_id = kid }, ct)).GetProperty("key").Clone();
                cache.Set("plaid-key:" + kid, key, TimeSpan.FromHours(1));
            }
            if (key.GetProperty("alg").GetString() != "ES256" || key.GetProperty("crv").GetString() != "P-256" ||
                key.GetProperty("kty").GetString() != "EC" || key.GetProperty("kid").GetString() != kid ||
                key.TryGetProperty("expired_at", out var expired) && expired.ValueKind != JsonValueKind.Null) return false;
            using var ec = ECDsa.Create(new ECParameters {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = WebEncoders.Base64UrlDecode(key.GetProperty("x").GetString()!),
                    Y = WebEncoders.Base64UrlDecode(key.GetProperty("y").GetString()!) }
            });
            if (!ec.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), WebEncoders.Base64UrlDecode(parts[2]),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return false;
            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
            var issued = payload.RootElement.GetProperty("iat").GetInt64();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (issued < now - 300 || issued > now + 30) return false;
            var claimed = Convert.FromHexString(payload.RootElement.GetProperty("request_body_sha256").GetString()!);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(body), claimed);
        }
        catch (Exception error) when (error is JsonException or FormatException or CryptographicException or
            KeyNotFoundException or InvalidOperationException or ArgumentException) { return false; }
    }
}
