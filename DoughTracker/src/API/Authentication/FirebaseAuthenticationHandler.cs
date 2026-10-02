using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace API.Authentication;

public sealed class FirebaseAuthenticationOptions : AuthenticationSchemeOptions
{
    public bool UseMockAuthentication { get; set; }
}

public sealed class FirebaseAuthenticationHandler(
    IOptionsMonitor<FirebaseAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITimeLimitedDataProtector mockTokens,
    IServiceProvider services) : AuthenticationHandler<FirebaseAuthenticationOptions>(options, logger, encoder)
{
    private bool unavailable;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return AuthenticateResult.NoResult();
        }

        if (Request.Headers.Authorization.Count != 1
            || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return AuthenticateResult.Fail("Invalid bearer token.");
        }

        try
        {
            string uid;
            if (Options.UseMockAuthentication)
            {
                uid = mockTokens.Unprotect(header.Parameter);
            }
            else
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Context.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var token = await services.GetRequiredService<FirebaseAuth>()
                    .VerifyIdTokenAsync(header.Parameter, timeout.Token);
                uid = token.Uid;
            }

            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, uid),
                new Claim("uid", uid),
            ], Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (FirebaseAuthException exception) when (exception.AuthErrorCode is
            AuthErrorCode.InvalidIdToken or AuthErrorCode.ExpiredIdToken or AuthErrorCode.RevokedIdToken)
        {
            return AuthenticateResult.Fail("Invalid or expired bearer token.");
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return AuthenticateResult.Fail("Invalid or expired bearer token.");
        }
        catch (Exception exception) when (exception is FirebaseAuthException or HttpRequestException
            || exception is OperationCanceledException && !Context.RequestAborted.IsCancellationRequested)
        {
            unavailable = true;
            Logger.LogWarning("Firebase token verification unavailable ({ErrorType}).", exception.GetType().Name);
            return AuthenticateResult.Fail("Authentication service unavailable.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (unavailable)
        {
            return Results.Problem(statusCode: 503, title: "Authentication service unavailable.").ExecuteAsync(Context);
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
