using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Application;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace API.IntegrationTests;

public class AuthenticationTests
{
    [Fact]
    public async Task MockTokensAuthenticateOnlyTheirProtectedUidAndRejectInvalidInput()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);

        foreach (var uid in new[] { "alice", "bob" })
        {
            client.DefaultRequestHeaders.Authorization = null;
            var response = await client.PostAsJsonAsync("/api/v1/dev/token", new { uid });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idToken").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
            var me = await client.GetAsync("/api/v1/me?owner_id=someone-else");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal(uid, (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uid").GetString());

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "x" + token[1..]);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        }

        var expired = factory.Services.GetRequiredService<ITimeLimitedDataProtector>()
            .Protect("alice", DateTimeOffset.UtcNow.AddMinutes(-1));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        foreach (var uid in new[] { "", "   ", new string('a', 129) })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/v1/dev/token", new { uid })).StatusCode);
        }
    }

    [Fact]
    public void MockAuthenticationCannotStartInProduction()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Firebase:UseMockAuthentication", "true");
        });
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Mock authentication is allowed only in Development", exception.Message);
    }

    [Fact]
    public async Task FirebaseAdminVerifiesSignaturesProjectIssuerAndExpiryWithoutAcceptingMockTokens()
    {
        using var key = RSA.Create(2048);
        using var wrongKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=DoughTracker test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certificates = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["test-key"] = certificate.ExportCertificatePem(),
        });
        var firebaseApp = FirebaseApp.Create(new AppOptions
        {
            ProjectId = "doughtracker-test",
            Credential = GoogleCredential.FromAccessToken("test-only-never-sent"),
            HttpClientFactory = new CertificateClientFactory(certificates),
        }, Guid.NewGuid().ToString());
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Firebase:ProjectId", "doughtracker-test");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<FirebaseAuth>();
                    services.AddSingleton(FirebaseAuth.GetAuth(firebaseApp));
                });
            });
            using var client = factory.CreateClient();
            var valid = SignedToken(key);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", valid);
            var me = await client.GetAsync("/api/v1/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal("alice", (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uid").GetString());
            Assert.Null(factory.Services.GetService<DemoWorkspace>());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/demo/workspace")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync("/api/v1/dev/token", new { uid = "alice" })).StatusCode);

            var mock = factory.Services.GetRequiredService<ITimeLimitedDataProtector>()
                .Protect("alice", TimeSpan.FromHours(1));
            foreach (var invalid in new[]
            {
                "not-a-token", mock, SignedToken(wrongKey), SignedToken(key, project: "another-project"),
                SignedToken(key, issuer: "https://wrong-issuer.example"),
                SignedToken(key, expiration: DateTimeOffset.UtcNow.AddHours(-1)),
                valid[..valid.LastIndexOf('.')], valid[..(valid.LastIndexOf('.') + 1)],
            })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invalid);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
            }
        }
        finally
        {
            firebaseApp.Delete();
        }
    }

    private static string SignedToken(RSA key, string project = "doughtracker-test",
        string? issuer = null, DateTimeOffset? expiration = null)
    {
        var header = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "test-key" }));
        var payload = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = "alice", aud = project, iss = issuer ?? $"https://securetoken.google.com/{project}",
            iat = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(),
            exp = (expiration ?? DateTimeOffset.UtcNow.AddHours(1)).ToUnixTimeSeconds(),
        }));
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    private sealed class CertificateClientFactory(string certificates) : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpClientHandler CreateHandler(CreateHttpClientArgs args) =>
            new CertificateHandler(certificates);
    }

    private sealed class CertificateHandler(string certificates) : HttpClientHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com",
                request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(certificates, Encoding.UTF8, "application/json"),
            };
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromHours(1) };
            return Task.FromResult(response);
        }
    }
}
