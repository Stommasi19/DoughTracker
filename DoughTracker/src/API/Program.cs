using System.Security.Claims;
using API.Authentication;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

var useMockAuthentication = builder.Configuration.GetValue<bool>("Firebase:UseMockAuthentication");
if (useMockAuthentication && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Mock authentication is allowed only in Development.");
}

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST")))
{
    throw new InvalidOperationException("Use the development mock instead of FIREBASE_AUTH_EMULATOR_HOST.");
}

if (!useMockAuthentication && string.IsNullOrWhiteSpace(builder.Configuration["Firebase:ProjectId"]))
{
    throw new InvalidOperationException("Set Firebase:ProjectId when using Firebase Admin authentication.");
}

builder.Services.AddOpenApi();
builder.Services.AddDataProtection();
builder.Services.AddSingleton(services => services.GetRequiredService<IDataProtectionProvider>()
    .CreateProtector("DoughTracker.MockFirebase.v1").ToTimeLimitedDataProtector());
builder.Services.AddSingleton(services =>
{
    var firebaseApp = FirebaseApp.Create(new AppOptions
    {
        ProjectId = builder.Configuration["Firebase:ProjectId"],
        Credential = GoogleCredential.GetApplicationDefault(),
    }, $"DoughTracker-{Guid.NewGuid()}");
    services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(firebaseApp.Delete);
    return FirebaseAuth.GetAuth(firebaseApp);
});
builder.Services.AddAuthentication("Firebase")
    .AddScheme<FirebaseAuthenticationOptions, FirebaseAuthenticationHandler>("Firebase", options =>
        options.UseMockAuthentication = useMockAuthentication);
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().Build());

var app = builder.Build();

if (!useMockAuthentication)
{
    app.Services.GetRequiredService<FirebaseAuth>();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
{
    uid = user.FindFirstValue(ClaimTypes.NameIdentifier),
}));

if (useMockAuthentication)
{
    api.MapPost("/dev/token", (MockTokenRequest request, ITimeLimitedDataProtector tokens, HttpResponse response) =>
    {
        if (string.IsNullOrWhiteSpace(request.Uid) || request.Uid.Length > 128)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["uid"] = ["Provide a non-empty UID of at most 128 characters."],
            });
        }

        response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            idToken = tokens.Protect(request.Uid, TimeSpan.FromHours(1)),
            tokenType = "Bearer",
            expiresIn = 3600,
        });
    }).AllowAnonymous();
}

app.Run();

public partial class Program;
public sealed record MockTokenRequest(string? Uid);
