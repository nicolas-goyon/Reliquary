using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Email;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Platform.Data;
using Reliquary.Platform.Email;
using Reliquary.Platform.Events;
using Reliquary.Platform.Extensions;
using Reliquary.Platform.Health;
using Reliquary.Platform.Jobs;
using Reliquary.Platform.Migrations;
using ConfigurationOptions = StackExchange.Redis.ConfigurationOptions;
using ConnectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer;
using IConnectionMultiplexer = StackExchange.Redis.IConnectionMultiplexer;

namespace Reliquary.Platform;

/// <summary>
/// Adds the platform services to a host.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services that all hosts use: database connections, shard directory, migrations and the outbox writer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddReliquaryPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));
        services.Configure<JobOptions>(configuration.GetSection(JobOptions.Section));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DataSources>();
        services.TryAddSingleton<IShardDirectory, ShardDirectory>();
        services.TryAddSingleton<IDatabase, Database>();
        services.TryAddSingleton<IDomainEventWriter, OutboxWriter>();
        services.TryAddSingleton<MigrationRunner>();
        services.TryAddSingleton<ExtensionRegistry>();

        if (string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.Section}:Smtp:Host"]))
        {
            services.TryAddSingleton<IEmailSender, LogEmailSender>();
        }
        else
        {
            services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        }

        // The migrations must complete before the other hosted services start, so this service comes first.
        services.Insert(0, ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, MigrateOnStartupService>());

        foreach (var set in PlatformMigrations.Sets)
        {
            services.AddSingleton(set);
        }

        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectRedis(configuration));
        services.TryAddSingleton(provider => new Lazy<IConnectionMultiplexer>(provider.GetRequiredService<IConnectionMultiplexer>));

        // All the replicas share the data protection keys, so a replica can read the cookies and the OAuth states of another replica.
        services.AddDataProtection().SetApplicationName("Reliquary");
        services.AddOptions<KeyManagementOptions>()
            .Configure<Lazy<IConnectionMultiplexer>>((options, redis) =>
                options.XmlRepository = new RedisXmlRepository(() => redis.Value.GetDatabase(), DataProtectionKeysKey));

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgresql", tags: [HealthTags.Ready])
            .AddCheck<RedisHealthCheck>("redis", tags: [HealthTags.Ready]);

        return services;
    }

    /// <summary>The Redis key of the data protection keys.</summary>
    public const string DataProtectionKeysKey = "reliquary:data-protection-keys";

    private static ConnectionMultiplexer ConnectRedis(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis") is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("The configuration has no connection string \"Redis\". Set ConnectionStrings:Redis.");

        // The connection continues in the background when Redis is not available, so the host can start.
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    }

    /// <summary>
    /// Adds the background services of the worker process: the outbox publisher and the scheduled job runner.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddReliquaryWorkerPlatform(this IServiceCollection services)
    {
        services.TryAddSingleton<OutboxPublisher>();
        services.TryAddSingleton<ScheduledJobRunner>();
        services.AddHostedService(provider => provider.GetRequiredService<OutboxPublisher>());
        services.AddHostedService(provider => provider.GetRequiredService<ScheduledJobRunner>());
        return services;
    }
}
