using System.Reflection;
using System.Text.Json;
using Npgsql;
using Reliquary.BuildingBlocks.Data;

namespace Reliquary.BuildingBlocks.Events;

/// <summary>
/// A versioned record of a change in a core component or a module.
/// </summary>
/// <remarks>
/// Each event type has a <see cref="DomainEventAttribute"/> that gives its name and its version.
/// See docs/architecture/domain-events.md.
/// </remarks>
public interface IDomainEvent;

/// <summary>
/// Gives the name and the version of a domain event type.
/// </summary>
/// <param name="type">The name of the event, in the form <c>&lt;component&gt;.&lt;entity&gt;.&lt;verb in the past tense&gt;</c>, for example <c>accounts.account.created</c>.</param>
/// <param name="version">The version of the payload. Increase it when a change to the payload breaks the consumers.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DomainEventAttribute(string type, int version) : Attribute
{
    /// <summary>Gets the name of the event.</summary>
    public string Type { get; } = type;

    /// <summary>Gets the version of the payload.</summary>
    public int Version { get; } = version;

    /// <summary>
    /// Gets the attribute of an event type.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The attribute.</returns>
    /// <exception cref="InvalidOperationException">The type has no <see cref="DomainEventAttribute"/>.</exception>
    public static DomainEventAttribute Of(Type eventType) =>
        eventType.GetCustomAttribute<DomainEventAttribute>()
        ?? throw new InvalidOperationException($"The event type {eventType.FullName} has no {nameof(DomainEventAttribute)}.");
}

/// <summary>
/// Writes domain events to the outbox of a database, in the transaction of the data change.
/// </summary>
public interface IDomainEventWriter
{
    /// <summary>
    /// Writes a domain event to the outbox of the database of the transaction.
    /// The publisher sends the event only after the transaction commits.
    /// </summary>
    /// <param name="target">The database group of the transaction. It gives the outbox table.</param>
    /// <param name="transaction">The transaction of the data change.</param>
    /// <param name="domainEvent">The event.</param>
    /// <param name="ownerId">The owner of the data, or <see langword="null"/> for data with no owner.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the outbox contains the event.</returns>
    Task WriteAsync(DatabaseTarget target, NpgsqlTransaction transaction, IDomainEvent domainEvent, Guid? ownerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A domain event as the consumers receive it.
/// </summary>
/// <param name="Id">The identifier of the event. Use it to ignore an event that arrives two times.</param>
/// <param name="Type">The name of the event.</param>
/// <param name="Version">The version of the payload.</param>
/// <param name="OwnerId">The owner of the data, or <see langword="null"/>.</param>
/// <param name="Payload">The payload of the event as JSON.</param>
/// <param name="OccurredAt">The time of the change.</param>
public sealed record DomainEventEnvelope(Guid Id, string Type, int Version, Guid? OwnerId, JsonElement Payload, DateTimeOffset OccurredAt)
{
    /// <summary>
    /// Reads the payload as an event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The event.</returns>
    public TEvent Read<TEvent>()
        where TEvent : IDomainEvent =>
        Payload.Deserialize<TEvent>(DomainEventJson.Options)
        ?? throw new InvalidOperationException($"The payload of the event {Id} is empty.");
}

/// <summary>
/// Receives domain events from the outbox publisher.
/// </summary>
/// <remarks>
/// The publisher can send an event more than one time, for example after a failure.
/// A consumer must give the same result when it receives an event two times.
/// </remarks>
public interface IDomainEventConsumer
{
    /// <summary>
    /// Tells whether the consumer receives events of a type and a version.
    /// </summary>
    /// <param name="type">The name of the event.</param>
    /// <param name="version">The version of the payload.</param>
    /// <returns><see langword="true"/> when the consumer receives these events.</returns>
    bool Accepts(string type, int version);

    /// <summary>
    /// Processes an event.
    /// </summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">A token that stops the operation.</param>
    /// <returns>A task that completes when the consumer processed the event.</returns>
    Task HandleAsync(DomainEventEnvelope domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// The JSON settings of the event payloads.
/// </summary>
public static class DomainEventJson
{
    /// <summary>
    /// Gets the JSON settings: camel case names, as in the public API.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
