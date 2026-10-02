using Application;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var useMockAuthentication = configuration.GetValue<bool>("Firebase:UseMockAuthentication");
        if (useMockAuthentication && !environment.IsDevelopment())
            throw new InvalidOperationException("Mock authentication is allowed only in Development.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST")))
            throw new InvalidOperationException("Use the development mock instead of FIREBASE_AUTH_EMULATOR_HOST.");
        if (!useMockAuthentication && string.IsNullOrWhiteSpace(configuration["Firebase:ProjectId"]))
            throw new InvalidOperationException("Set Firebase:ProjectId when using Firebase Admin authentication.");

        services.AddDataProtection();
        services.AddSingleton(provider => provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("DoughTracker.MockFirebase.v1").ToTimeLimitedDataProtector());
        services.AddSingleton(provider =>
        {
            var firebaseApp = FirebaseApp.Create(new AppOptions
            {
                ProjectId = configuration["Firebase:ProjectId"],
                Credential = GoogleCredential.GetApplicationDefault(),
            }, $"DoughTracker-{Guid.NewGuid()}");
            provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(firebaseApp.Delete);
            return FirebaseAuth.GetAuth(firebaseApp);
        });
        services.AddAuthentication("Firebase")
            .AddScheme<FirebaseAuthenticationOptions, FirebaseAuthenticationHandler>("Firebase", options =>
                options.UseMockAuthentication = useMockAuthentication);
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
        return services.AddLedger(configuration, environment).AddConnections(configuration, environment);
    }

    public static IServiceCollection AddMockInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DemoWorkspace(PlaidDemoData.Seed()));
        return services;
    }

    public static void InitializeInfrastructure(this IServiceProvider services)
    {
        if (!services.GetRequiredService<IConfiguration>().GetValue<bool>("Firebase:UseMockAuthentication"))
            services.GetRequiredService<FirebaseAuth>();
    }
}
