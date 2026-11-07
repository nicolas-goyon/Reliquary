using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Platform.Data;

namespace Reliquary.Platform.Events;

/// <summary>
/// The settings of the outbox publisher. The section is <c>Outbox</c>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>The name of the configuration section.</summary>
    public const string Section = "Outbox";

    /// <summary>Gets or sets the time between two reads when the outboxes are empty.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the maximum number of events in one read.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Gets or sets the number of failed attempts after which the publisher stops sending an event.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Gets or sets the time that the outbox keeps a published event before it deletes it.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>
/// Reads the outbox tables of all databases and sends each new event to its consumers.
/// </summary>
/// <remarks>
/// The publisher sends each event at least one time. After a failure, it waits before the next attempt: see <see cref="RetryDelay"/>. Several worker replicas can run the publisher at the same time:
/// a row lock stops two replicas that send the same event. The order of the events is the order of their identifiers
/// in each database, but a failure can change the order.
/// </remarks>
/// <param name="sources">The connection settings.</param>
/// <param name="consumers">The event consumers.</param>
/// <param name="options">The settings of the publisher.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class OutboxPublisher(
    DataSources sources,
    IEnumerable<IDomainEventConsumer> consumers,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly IDomainEventConsumer[] consumers = [.. consumers];

    /// <summary>
    /// Sends all the events that wait in the outboxes, then returns.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>The number of events that the publisher sent.</returns>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        foreach (var (connectionString, table) in Outboxes())
        {
            int count;
            do
            {
                count = await PublishBatchAsync(connectionString, table, cancellationToken);
                total += count;
            }
            while (count > 0);
        }

        return total;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await PublishPendingAsync(stoppingToken);
                if (time.GetUtcNow() - lastCleanup > TimeSpan.FromHours(1))
                {
                    await DeleteOldEventsAsync(stoppingToken);
                    lastCleanup = time.GetUtcNow();
                }

                if (count == 0)
                {
                    await Task.Delay(options.Value.PollInterval, time, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The publisher must continue after an error, for example when a database is not available.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.OutboxPublisherFailed(exception, options.Value.PollInterval);
                await Task.Delay(options.Value.PollInterval, time, stoppingToken);
            }
        }
    }

    private IEnumerable<(string ConnectionString, string Table)> Outboxes()
    {
        yield return (sources.Users, OutboxTables.For(DatabaseTarget.Users));
        yield return (sources.Reference, OutboxTables.For(DatabaseTarget.Reference));
        foreach (var shard in sources.Shards.Values)
        {
            yield return (shard, OutboxTables.For(DatabaseTarget.Content));
        }
    }

    private async Task<int> PublishBatchAsync(string connectionString, string table, CancellationToken cancellationToken)
    {
        await using var connection = await sources.Pool(connectionString).OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<OutboxRow>(new CommandDefinition(
            $"""
            select id as Id, event_type as Type, event_version as Version, owner_id as OwnerId,
                   payload::text as Payload, occurred_at as OccurredAt, attempts as Attempts
            from {table}
            where published_at is null and attempts < @maxAttempts and available_at <= @now
            order by id
            limit @batchSize
            for update skip locked
            """,
            new { maxAttempts = options.Value.MaxAttempts, batchSize = options.Value.BatchSize, now = time.GetUtcNow() },
            transaction,
            cancellationToken: cancellationToken))).ToList();

        foreach (var row in rows)
        {
            using var payload = JsonDocument.Parse(row.Payload);
            var envelope = new DomainEventEnvelope(row.Id, row.Type, row.Version, row.OwnerId, payload.RootElement.Clone(), row.OccurredAt);

            try
            {
                foreach (var consumer in consumers.Where(consumer => consumer.Accepts(row.Type, row.Version)))
                {
                    await consumer.HandleAsync(envelope, cancellationToken);
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    $"update {table} set published_at = @now, attempts = attempts + 1, last_error = null where id = @id",
                    new { now = time.GetUtcNow(), id = row.Id },
                    transaction,
                    cancellationToken: cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.EventConsumerFailed(exception, row.Id, row.Type);
                await connection.ExecuteAsync(new CommandDefinition(
                    $"update {table} set attempts = attempts + 1, last_error = @error, available_at = @availableAt where id = @id",
                    new { error = exception.Message, id = row.Id, availableAt = time.GetUtcNow() + RetryDelay(row.Attempts + 1) },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    private async Task DeleteOldEventsAsync(CancellationToken cancellationToken)
    {
        var limit = time.GetUtcNow() - options.Value.Retention;
        foreach (var (connectionString, table) in Outboxes().Distinct())
        {
            await using var connection = await sources.Pool(connectionString).OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                $"delete from {table} where published_at < @limit",
                new { limit },
                cancellationToken: cancellationToken));
        }
    }

    /// <summary>
    /// Gets the delay before the next attempt after a failure: 2 to the power of the attempt number, in seconds, and one hour at most.
    /// </summary>
    /// <param name="attempts">The number of failed attempts.</param>
    /// <returns>The delay.</returns>
    internal static TimeSpan RetryDelay(int attempts) => TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, attempts)));

    private sealed record OutboxRow(Guid Id, string Type, int Version, Guid? OwnerId, string Payload, DateTime OccurredAt, int Attempts);
}
