using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Reliquary.BuildingBlocks;
using Reliquary.BuildingBlocks.Data;
using Reliquary.BuildingBlocks.Events;

namespace Reliquary.Platform.Events;

/// <summary>
/// Writes domain events to the outbox table of a database.
/// </summary>
/// <param name="time">The clock.</param>
internal sealed class OutboxWriter(TimeProvider time) : IDomainEventWriter
{
    private static readonly ConcurrentDictionary<Type, DomainEventAttribute> Attributes = new();

    public async Task WriteAsync(DatabaseTarget target, NpgsqlTransaction transaction, IDomainEvent domainEvent, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(domainEvent);

        var attribute = Attributes.GetOrAdd(domainEvent.GetType(), DomainEventAttribute.Of);
        var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), DomainEventJson.Options);
        var sql = $"""
            insert into {OutboxTables.For(target)} (id, event_type, event_version, owner_id, payload, occurred_at, available_at)
            values (@id, @type, @version, @ownerId, @payload, @occurredAt, @occurredAt)
            """;

        var parameters = new DynamicParameters();
        parameters.Add("id", Ids.New());
        parameters.Add("type", attribute.Type);
        parameters.Add("version", attribute.Version);
        parameters.Add("ownerId", ownerId);
        parameters.Add("payload", new JsonbParameter(payload));
        parameters.Add("occurredAt", time.GetUtcNow());

        await transaction.Connection!.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
    }

    private sealed class JsonbParameter(string json) : SqlMapper.ICustomQueryParameter
    {
        public void AddParameter(System.Data.IDbCommand command, string name)
        {
            command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json });
        }
    }
}

/// <summary>
/// Gives the outbox table of each database group.
/// </summary>
internal static class OutboxTables
{
    /// <summary>
    /// Gets the outbox table of a database group.
    /// </summary>
    /// <param name="target">The database group.</param>
    /// <returns>The qualified name of the outbox table.</returns>
    public static string For(DatabaseTarget target) => target switch
    {
        DatabaseTarget.Users => "users.outbox",
        DatabaseTarget.Reference => "reference.outbox",
        DatabaseTarget.Content => "content.outbox",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "The database group is not known."),
    };
}
