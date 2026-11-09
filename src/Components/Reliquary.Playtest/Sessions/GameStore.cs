using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reliquary.Playtest.Engine;
using StackExchange.Redis;

namespace Reliquary.Playtest.Sessions;

/// <summary>
/// The state of an online game as the store keeps it, with its sequence number.
/// </summary>
/// <typeparam name="T">The type of the game state.</typeparam>
/// <param name="Seq">The sequence number of the last change. A new game has the number 0.</param>
/// <param name="State">The state.</param>
public sealed record StoredGame<T>(long Seq, T State);

/// <summary>
/// The result of a write to a game.
/// </summary>
public enum GameWriteStatus
{
    /// <summary>The store wrote the new state with a new sequence number.</summary>
    Written,

    /// <summary>The game changed since the caller read it. Read the game again and apply the action again.</summary>
    Conflict,

    /// <summary>The store applied this action of this seat before. The result gives the sequence number of the first write.</summary>
    Duplicate,

    /// <summary>The game does not exist, or it expired.</summary>
    NotFound,
}

/// <summary>
/// The result of a write to a game.
/// </summary>
/// <param name="Status">The result.</param>
/// <param name="Seq">
/// For <see cref="GameWriteStatus.Written"/> and <see cref="GameWriteStatus.Duplicate"/>, the sequence number of the change.
/// For <see cref="GameWriteStatus.Conflict"/>, the current sequence number. Else 0.
/// </param>
public sealed record GameWriteResult(GameWriteStatus Status, long Seq);

/// <summary>
/// One change of a game, for the action stream.
/// </summary>
/// <param name="Seat">The seat that did the action, or <see langword="null"/> for a change of the server.</param>
/// <param name="ClientActionId">
/// The ID that the client gave to the action, or <see langword="null"/>. The store keeps the last IDs of each seat,
/// so a retry of the same action does not apply it two times.
/// </param>
/// <param name="Action">The action as JSON, for the action stream.</param>
public sealed record GameChange(int? Seat, string? ClientActionId, string Action);

/// <summary>
/// The settings of the game store, in the section <c>Playtest:Store</c>.
/// </summary>
public sealed class GameStoreOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Playtest:Store";

    /// <summary>Gets or sets the time with no change after which the store deletes a game. The default is 1 hour.</summary>
    public TimeSpan IdleLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets the approximate maximum number of entries of the action stream of a game. The default is 10,000.</summary>
    public int ActionStreamLength { get; set; } = 10_000;

    /// <summary>Gets or sets the number of client action IDs that the store keeps for each seat. The default is 64.</summary>
    public int ClientActionIdsPerSeat { get; set; } = 64;
}

/// <summary>
/// Keeps the online games in Redis, so all the API replicas share them. See docs/decisions/0047-trusted-server-for-online-games.md.
/// </summary>
/// <remarks>
/// <para>
/// Each game has these keys. They have the same hash tag <c>{id}</c>, so one Lua script can change all of them.
/// </para>
/// <list type="bullet">
/// <item><c>reliquary:playtest:game:{id}:state</c>: the state as JSON.</item>
/// <item><c>reliquary:playtest:game:{id}:seq</c>: the sequence number of the last change.</item>
/// <item><c>reliquary:playtest:game:{id}:log</c>: the action stream, with an approximate maximum length.</item>
/// <item><c>reliquary:playtest:game:{id}:actions</c>: the recent client action IDs of each seat, with their sequence numbers.
/// A ring of fixed size for each seat removes the oldest ID.</item>
/// </list>
/// <para>
/// A write is a compare-and-set: the store writes the new state only when the sequence number did not change since the read.
/// Each write sets the idle lifetime again on all the keys.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the game state. The store writes it as JSON with <see cref="TableJson.Options"/>.</typeparam>
internal sealed class GameStore<T>(Lazy<IConnectionMultiplexer> redis, IOptions<GameStoreOptions> options)
    where T : class
{
    private static readonly JsonSerializerOptions Json = TableJson.Options;

    /// <summary>The start of all the Redis keys of the online games.</summary>
    public const string KeyPrefix = "reliquary:playtest:game:";

    // KEYS: 1 state, 2 seq, 3 log. ARGV: 1 state, 2 TTL in milliseconds, 3 log entry.
    private const string CreateScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then
          return 0
        end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        redis.call('SET', KEYS[2], 0, 'PX', ARGV[2])
        redis.call('XADD', KEYS[3], '0-1', 'seq', 0, 'action', ARGV[3])
        redis.call('PEXPIRE', KEYS[3], ARGV[2])
        return 1
        """;

    // KEYS: 1 state, 2 seq, 3 log, 4 action IDs.
    // ARGV: 1 expected seq, 2 state, 3 TTL in milliseconds, 4 log entry, 5 seat ('' for the server),
    // 6 client action ID ('' for none), 7 stream length, 8 IDs per seat.
    // Returns {status, seq}: 1 written, 2 conflict, 3 duplicate, 4 not found.
    // The hash of the action IDs has these fields: "id:{seat}:{client action ID}" = seq of the action,
    // "slot:{seat}:{n}" = the client action ID in the slot n of the ring of the seat, "next:{seat}" = the count of IDs of the seat.
    private const string WriteScript = """
        local current = redis.call('GET', KEYS[2])
        if not current then
          return {4, 0}
        end
        local id_field = 'id:' .. ARGV[5] .. ':' .. ARGV[6]
        if ARGV[6] ~= '' then
          local previous = redis.call('HGET', KEYS[4], id_field)
          if previous then
            return {3, tonumber(previous)}
          end
        end
        if current ~= ARGV[1] then
          return {2, tonumber(current)}
        end
        local seq = redis.call('INCR', KEYS[2])
        redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
        redis.call('PEXPIRE', KEYS[2], ARGV[3])
        redis.call('XADD', KEYS[3], 'MAXLEN', '~', ARGV[7], seq .. '-1', 'seq', seq, 'seat', ARGV[5], 'action', ARGV[4])
        redis.call('PEXPIRE', KEYS[3], ARGV[3])
        if ARGV[6] ~= '' then
          local count = redis.call('HINCRBY', KEYS[4], 'next:' .. ARGV[5], 1)
          local slot_field = 'slot:' .. ARGV[5] .. ':' .. (count % tonumber(ARGV[8]))
          local oldest = redis.call('HGET', KEYS[4], slot_field)
          if oldest then
            redis.call('HDEL', KEYS[4], 'id:' .. ARGV[5] .. ':' .. oldest)
          end
          redis.call('HSET', KEYS[4], slot_field, ARGV[6], id_field, seq)
        end
        redis.call('PEXPIRE', KEYS[4], ARGV[3])
        return {1, seq}
        """;

    // KEYS: 1 state, 2 seq, 3 log, 4 action IDs.
    private const string DeleteScript = """
        return redis.call('DEL', KEYS[1], KEYS[2], KEYS[3], KEYS[4])
        """;

    // KEYS: 1 state, 2 seq. Reads the two keys in one step, so the state and the number agree.
    private const string ReadScript = """
        return {redis.call('GET', KEYS[2]), redis.call('GET', KEYS[1])}
        """;

    /// <summary>The highest seat number. The seats have the numbers 0 to <see cref="MaxSeat"/>.</summary>
    public const int MaxSeat = 15;

    /// <summary>The maximum length of a client action ID.</summary>
    public const int MaxClientActionIdLength = 64;

    private GameStoreOptions Settings => options.Value;

    /// <summary>Gets the key of the state of a game.</summary>
    public static string StateKey(string gameId) => $"{KeyPrefix}{{{gameId}}}:state";

    /// <summary>Gets the key of the sequence number of a game.</summary>
    public static string SeqKey(string gameId) => $"{KeyPrefix}{{{gameId}}}:seq";

    /// <summary>Gets the key of the action stream of a game.</summary>
    public static string LogKey(string gameId) => $"{KeyPrefix}{{{gameId}}}:log";

    /// <summary>Gets the key of the recent client action IDs of a game.</summary>
    public static string ActionsKey(string gameId) => $"{KeyPrefix}{{{gameId}}}:actions";

    /// <summary>
    /// Creates a game with the sequence number 0.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <param name="state">The first state.</param>
    /// <param name="change">The change that created the game, for the action stream.</param>
    /// <returns><see langword="true"/> when the store created the game; <see langword="false"/> when a game with this ID exists.</returns>
    public async Task<bool> CreateAsync(string gameId, T state, GameChange change)
    {
        CheckId(gameId);
        ArgumentNullException.ThrowIfNull(change);
        var result = await Database.ScriptEvaluateAsync(
            CreateScript,
            [StateKey(gameId), SeqKey(gameId), LogKey(gameId)],
            [JsonSerializer.Serialize(state, Json), Ttl, change.Action]);
        return (long)result == 1;
    }

    /// <summary>
    /// Reads a game.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <returns>The game, or <see langword="null"/> when the game does not exist or expired.</returns>
    public async Task<StoredGame<T>?> GetAsync(string gameId)
    {
        CheckId(gameId);
        var values = (RedisResult[])(await Database.ScriptEvaluateAsync(ReadScript, [StateKey(gameId), SeqKey(gameId)]))!;
        if (values[0].IsNull || values[1].IsNull)
        {
            return null;
        }

        var state = JsonSerializer.Deserialize<T>((string)values[1]!, Json)
            ?? throw new InvalidOperationException($"The state of the game {gameId} in Redis is null.");
        return new StoredGame<T>(long.Parse((string)values[0]!, CultureInfo.InvariantCulture), state);
    }

    /// <summary>
    /// Writes a new state of a game, when its sequence number is still <paramref name="expectedSeq"/>.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <param name="expectedSeq">The sequence number of the state that the caller read.</param>
    /// <param name="state">The new state.</param>
    /// <param name="change">The change, for the action stream and for the client action IDs.</param>
    /// <returns>The result of the write.</returns>
    public async Task<GameWriteResult> WriteAsync(string gameId, long expectedSeq, T state, GameChange change)
    {
        CheckId(gameId);
        ArgumentNullException.ThrowIfNull(change);
        if (change.Seat is < 0 or > MaxSeat)
        {
            throw new ArgumentOutOfRangeException(nameof(change), change.Seat, $"The seat must be from 0 to {MaxSeat}.");
        }

        if (change.ClientActionId is { Length: > MaxClientActionIdLength })
        {
            throw new ArgumentOutOfRangeException(nameof(change), change.ClientActionId.Length, $"A client action ID must have {MaxClientActionIdLength} characters or less.");
        }

        var seat = change.Seat?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var clientActionId = change.Seat is null ? string.Empty : change.ClientActionId ?? string.Empty;
        var values = (RedisResult[])(await Database.ScriptEvaluateAsync(
            WriteScript,
            [StateKey(gameId), SeqKey(gameId), LogKey(gameId), ActionsKey(gameId)],
            [
                expectedSeq,
                JsonSerializer.Serialize(state, Json),
                Ttl,
                change.Action,
                seat,
                clientActionId,
                Settings.ActionStreamLength,
                Settings.ClientActionIdsPerSeat,
            ]))!;
        var status = (long)values[0] switch
        {
            1 => GameWriteStatus.Written,
            2 => GameWriteStatus.Conflict,
            3 => GameWriteStatus.Duplicate,
            _ => GameWriteStatus.NotFound,
        };
        return new GameWriteResult(status, (long)values[1]);
    }

    /// <summary>
    /// Reads the action stream of a game, from the oldest entry that the stream keeps.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <param name="count">The maximum number of entries.</param>
    /// <returns>The entries: the sequence number, the seat (or <see langword="null"/>) and the action.</returns>
    public async Task<IReadOnlyList<(long Seq, int? Seat, string Action)>> ReadLogAsync(string gameId, int count)
    {
        CheckId(gameId);
        var entries = await Database.StreamRangeAsync(LogKey(gameId), count: count);
        return [.. entries.Select(entry =>
        {
            var seat = entry["seat"];
            return (
                long.Parse((string)entry["seq"]!, CultureInfo.InvariantCulture),
                seat.IsNullOrEmpty ? (int?)null : int.Parse((string)seat!, CultureInfo.InvariantCulture),
                (string)entry["action"]!);
        })];
    }

    /// <summary>
    /// Deletes a game and all its keys.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <returns><see langword="true"/> when the game existed.</returns>
    public async Task<bool> DeleteAsync(string gameId)
    {
        CheckId(gameId);
        var deleted = await Database.ScriptEvaluateAsync(
            DeleteScript,
            [StateKey(gameId), SeqKey(gameId), LogKey(gameId), ActionsKey(gameId)]);
        return (long)deleted > 0;
    }

    private IDatabase Database => redis.Value.GetDatabase();

    private long Ttl => (long)Settings.IdleLifetime.TotalMilliseconds;

    private static void CheckId(string gameId)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameId);
        if (gameId.Length > 64 || !gameId.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new ArgumentException("The game ID must have 1 to 64 ASCII letters, digits or hyphens.", nameof(gameId));
        }
    }
}
