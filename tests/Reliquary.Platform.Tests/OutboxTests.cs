using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;
using Reliquary.Platform.Events;

namespace Reliquary.Platform.Tests;

public sealed class OutboxTests
{
    [Fact]
    public async Task CommittedChangePublishesItsEvent()
    {
        var consumer = new RecordingConsumer();
        await using var host = await PlatformHost.CreateAsync(configure: services => services.AddSingleton<IDomainEventConsumer>(consumer));
        var owner = await AssignOwnerAsync(host);

        await ChangeAsync(host, owner, "first", commit: true);
        var published = await host.Get<OutboxPublisher>().PublishPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, published);
        var received = Assert.Single(consumer.Received);
        Assert.Equal("tests.item.created", received.Type);
        Assert.Equal(1, received.Version);
        Assert.Equal(owner, received.OwnerId);
        Assert.Equal("first", received.Read<ItemCreated>().Name);
    }

    [Fact]
    public async Task RolledBackChangePublishesNoEvent()
    {
        var consumer = new RecordingConsumer();
        await using var host = await PlatformHost.CreateAsync(configure: services => services.AddSingleton<IDomainEventConsumer>(consumer));
        var owner = await AssignOwnerAsync(host);

        await ChangeAsync(host, owner, "rolled back", commit: false);
        var published = await host.Get<OutboxPublisher>().PublishPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, published);
        Assert.Empty(consumer.Received);
        Assert.Equal(0, await CountItemsAsync(host));
    }

    [Fact]
    public async Task FailedEventWaitsForItsRetryDelayAndArrivesAgain()
    {
        var consumer = new RecordingConsumer { FailuresBeforeSuccess = 1 };
        await using var host = await PlatformHost.CreateAsync(configure: services => services.AddSingleton<IDomainEventConsumer>(consumer));
        var owner = await AssignOwnerAsync(host);
        await ChangeAsync(host, owner, "retry", commit: true);
        var publisher = host.Get<OutboxPublisher>();

        await publisher.PublishPendingAsync(TestContext.Current.CancellationToken);
        var (attempts, error, publishedAt) = await OutboxStateAsync(host);
        Assert.Equal(1, attempts);
        Assert.NotNull(error);
        Assert.Null(publishedAt);

        Assert.Equal(0, await publisher.PublishPendingAsync(TestContext.Current.CancellationToken));

        host.Time.Advance(OutboxPublisher.RetryDelay(1));
        await publisher.PublishPendingAsync(TestContext.Current.CancellationToken);
        (attempts, error, publishedAt) = await OutboxStateAsync(host);
        Assert.Equal(2, attempts);
        Assert.Null(error);
        Assert.NotNull(publishedAt);
        Assert.Single(consumer.Received);
    }

    [Fact]
    public async Task ConsumerReceivesOnlyTheTypesThatItAccepts()
    {
        var consumer = new RecordingConsumer { AcceptedType = "tests.other.created" };
        await using var host = await PlatformHost.CreateAsync(configure: services => services.AddSingleton<IDomainEventConsumer>(consumer));
        var owner = await AssignOwnerAsync(host);
        await ChangeAsync(host, owner, "ignored", commit: true);

        var published = await host.Get<OutboxPublisher>().PublishPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, published);
        Assert.Empty(consumer.Received);
    }

    private static async Task<Guid> AssignOwnerAsync(PlatformHost host)
    {
        var owner = Ids.New();
        await using var connection = await host.Get<IDatabase>().OpenUsersAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await host.Get<IShardDirectory>().AssignAsync(owner, transaction, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return owner;
    }

    private static async Task ChangeAsync(PlatformHost host, Guid owner, string name, bool commit)
    {
        await using var connection = await host.Get<IDatabase>().OpenContentAsync(owner, ContentAccess.Write, TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var id = Ids.New();
        await connection.ExecuteAsync(
            "insert into content.test_items (id, owner_id, name) values (@id, @owner, @name)",
            new { id, owner, name },
            transaction);
        await host.Get<IDomainEventWriter>().WriteAsync(
            DatabaseTarget.Content, transaction, new ItemCreated(id, name), owner, TestContext.Current.CancellationToken);

        if (commit)
        {
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task<int> CountItemsAsync(PlatformHost host)
    {
        await using var connection = new NpgsqlConnection(host.Databases[0].ConnectionString);
        return await connection.ExecuteScalarAsync<int>("select count(*) from content.test_items");
    }

    private static async Task<(int Attempts, string? Error, DateTime? PublishedAt)> OutboxStateAsync(PlatformHost host)
    {
        await using var connection = new NpgsqlConnection(host.Databases[0].ConnectionString);
        return await connection.QuerySingleAsync<(int, string?, DateTime?)>(
            "select attempts, last_error, published_at from content.outbox");
    }

    [DomainEvent("tests.item.created", 1)]
    private sealed record ItemCreated(Guid ItemId, string Name) : IDomainEvent;

    private sealed class RecordingConsumer : IDomainEventConsumer
    {
        public ConcurrentQueue<DomainEventEnvelope> Received { get; } = new();

        public string AcceptedType { get; init; } = "tests.item.created";

        public int FailuresBeforeSuccess { get; set; }

        public bool Accepts(string type, int version) => type == AcceptedType && version == 1;

        public Task HandleAsync(DomainEventEnvelope domainEvent, CancellationToken cancellationToken)
        {
            if (FailuresBeforeSuccess > 0)
            {
                FailuresBeforeSuccess--;
                throw new InvalidOperationException("The consumer fails on purpose.");
            }

            Received.Enqueue(domainEvent);
            return Task.CompletedTask;
        }
    }
}
