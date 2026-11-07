using System.Reflection;
using System.Text.Json;

namespace Reliquary.BuildingBlocks.Events;

/// <summary>
/// One field of the payload of a domain event.
/// </summary>
/// <param name="Name">The name of the field in the JSON payload, in camel case.</param>
/// <param name="Type">The JSON type: <c>uuid</c>, <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>date-time</c>, <c>date</c>, <c>array</c> or <c>object</c>.</param>
/// <param name="Nullable">Whether the value can be <see langword="null"/>.</param>
public sealed record DomainEventField(string Name, string Type, bool Nullable);

/// <summary>
/// One event type of the catalog.
/// </summary>
/// <param name="Type">The name of the event, for example <c>accounts.account.created</c>.</param>
/// <param name="Version">The version of the payload.</param>
/// <param name="Fields">The fields of the payload, in the order of the declaration.</param>
public sealed record DomainEventType(string Type, int Version, IReadOnlyList<DomainEventField> Fields);

/// <summary>
/// The list of the domain event types that modules can consume.
/// The catalog comes from the <see cref="DomainEventAttribute"/> of the event types in the assemblies of the core components.
/// </summary>
public sealed class DomainEventCatalog
{
    private DomainEventCatalog(IReadOnlyList<DomainEventType> types) => Types = types;

    /// <summary>Gets the event types, sorted by name, then by version.</summary>
    public IReadOnlyList<DomainEventType> Types { get; }

    /// <summary>
    /// Finds the event types in assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies of the core components.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidOperationException">Two event types have the same name and the same version.</exception>
    public static DomainEventCatalog FromAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var types = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<DomainEventAttribute>()))
            .Where(item => item.Attribute is not null)
            .Select(item => new DomainEventType(item.Attribute!.Type, item.Attribute.Version, FieldsOf(item.Type)))
            .OrderBy(type => type.Type, StringComparer.Ordinal)
            .ThenBy(type => type.Version)
            .ToList();

        var duplicate = types.GroupBy(type => (type.Type, type.Version)).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two event types have the name {duplicate.Key.Type} and the version {duplicate.Key.Version}. Give each event type a different name or version.");
        }

        return new DomainEventCatalog(types);
    }

    private static List<DomainEventField> FieldsOf(Type eventType)
    {
        // A NullabilityInfoContext is not thread-safe, and several hosts can start at the same time, so each call has its own context.
        var nullability = new NullabilityInfoContext();
        return eventType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken)
            .Select(property => new DomainEventField(
                JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                JsonType(property.PropertyType),
                IsNullable(property, nullability)))
            .ToList();
    }

    private static bool IsNullable(PropertyInfo property, NullabilityInfoContext nullability) =>
        property.PropertyType.IsValueType
            ? Nullable.GetUnderlyingType(property.PropertyType) is not null
            : nullability.Create(property).ReadState == NullabilityState.Nullable;

    private static string JsonType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            return "string";
        }

        if (type == typeof(Guid))
        {
            return "uuid";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            return "integer";
        }

        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
        {
            return "number";
        }

        if (type == typeof(DateTimeOffset) || type == typeof(DateTime))
        {
            return "date-time";
        }

        if (type == typeof(DateOnly))
        {
            return "date";
        }

        return typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ? "array" : "object";
    }
}
