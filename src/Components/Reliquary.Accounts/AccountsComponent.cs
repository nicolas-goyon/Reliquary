using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reliquary.Accounts.Auth;
using Reliquary.Accounts.Data;
using Reliquary.Accounts.Emails;
using Reliquary.Accounts.PersonalData;
using Reliquary.Accounts.Profile;
using Reliquary.Accounts.Quotas;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Demo;
using Reliquary.BuildingBlocks.Jobs;

namespace Reliquary.Accounts;

/// <summary>
/// Registers the Accounts core component: accounts, sign-in, web sessions, OAuth providers, API keys and quotas.
/// </summary>
public sealed class AccountsComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Accounts";

    /// <inheritdoc />
    public IEnumerable<MigrationSet> MigrationSets =>
    [
        new(DatabaseTarget.Users, "users", "accounts", typeof(AccountsComponent).Assembly),
    ];

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.Configure<QuotaOptions>(configuration.GetSection(QuotaOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AccountStore>();
        services.AddSingleton<IAccountDirectory, AccountDirectory>();
        services.AddSingleton<IPersonalDataProvider, AccountPersonalData>();
        services.AddSingleton<PersonalDataService>();
        services.AddSingleton<AccessTokens>();
        services.AddSingleton<AccountEmails>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<IDemoDataSeeder, DemoAccounts>();
    }

    /// <inheritdoc />
    public void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<QuotaCounter>();
        services.AddSingleton<QuotaFilter>();

        services.AddAccountsAuthentication(configuration);

        var requestsPerMinute = configuration.GetValue($"{AuthOptions.Section}:{nameof(AuthOptions.RequestsPerMinute)}", new AuthOptions().RequestsPerMinute);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthEndpoints.RateLimiterPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await ApiProblems.Problem(
                    StatusCodes.Status429TooManyRequests,
                    ErrorCodes.RateLimited,
                    "The client sent too many requests to the authentication endpoints. Wait one minute, then try again.")
                    .ExecuteAsync(context.HttpContext);
            };
        });
    }

    /// <inheritdoc />
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IScheduledJob, UsageFlushJob>();
        services.AddSingleton<IScheduledJob, PersonalDataJob>();
    }

    /// <inheritdoc />
    public void ConfigureApi(RouteGroupBuilder api) => api.AddEndpointFilter<QuotaFilter>();

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        AuthEndpoints.Map(api);
        ExternalEndpoints.Map(api);
        ProfileEndpoints.Map(api);
        PersonalDataEndpoints.Map(api);
    }

    /// <inheritdoc />
    public void MapInternalEndpoints(IEndpointRouteBuilder internalApi) => InternalProfileEndpoints.Map(internalApi);
}
