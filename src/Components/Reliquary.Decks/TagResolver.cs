using Npgsql;
using Reliquary.BuildingBlocks;
using Reliquary.Decks.Data;

namespace Reliquary.Decks;

/// <summary>
/// Finds the tags of deck lines by name, and creates the tags that do not exist.
/// </summary>
internal static class TagResolver
{
    /// <summary>
    /// Finds the tags of the inputs by name, and creates the tags that do not exist.
    /// </summary>
    public static async Task<TagMap> ResolveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ownerId, Guid deckId, IReadOnlyList<LineInput> inputs, TimeProvider time, CancellationToken cancellationToken)
    {
        var existing = await DeckStore.ListTagsAsync(connection, ownerId, deckId, cancellationToken, transaction);
        var map = new TagMap(existing);
        var missing = new List<TagRow>();
        foreach (var (name, global) in inputs.SelectMany(input => input.Tags.Select(tag => (tag, false)).Concat(input.GlobalTags.Select(tag => (tag, true)))))
        {
            if (map.Find(name, global) is null)
            {
                var tag = new TagRow { Id = Ids.New(), DeckId = global ? null : deckId, Name = name.Trim() };
                missing.Add(tag);
                map.Add(tag);
            }
        }

        await DeckStore.InsertTagsAsync(transaction, ownerId, missing, time.GetUtcNow().UtcDateTime, cancellationToken);
        return map;
    }

    /// <summary>
    /// Gets the tags that the lines use.
    /// </summary>
    public static async Task<IReadOnlyList<TagRow>> TagsOfLinesAsync(NpgsqlConnection connection, IReadOnlyList<DeckLineRow> lines, CancellationToken cancellationToken)
    {
        var ids = lines.SelectMany(line => line.TagIds).Distinct().ToList();
        return ids.Count == 0 ? [] : await DeckStore.FindTagsAsync(connection, ids, cancellationToken);
    }
}

/// <summary>
/// The tags of an owner and of one deck, by name. The names are case-insensitive.
/// </summary>
internal sealed class TagMap
{
    private readonly Dictionary<string, TagRow> local = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TagRow> global = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> localIds = [];

    public TagMap(IEnumerable<TagRow> tags)
    {
        foreach (var tag in tags)
        {
            Add(tag);
        }
    }

    public void Add(TagRow tag)
    {
        (tag.IsGlobal ? global : local)[tag.Name] = tag;
        if (!tag.IsGlobal)
        {
            localIds.Add(tag.Id);
        }
    }

    public TagRow? Find(string name, bool isGlobal) =>
        (isGlobal ? global : local).GetValueOrDefault(name.Trim());

    public bool IsLocal(Guid tagId) => localIds.Contains(tagId);

    public IEnumerable<Guid> IdsOf(LineInput input) =>
        input.Tags.Select(name => Find(name, false)!.Id).Concat(input.GlobalTags.Select(name => Find(name, true)!.Id)).Distinct();
}
