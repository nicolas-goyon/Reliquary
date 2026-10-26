using System.Globalization;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reliquary.Accounts.Data;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Jobs;
using Reliquary.BuildingBlocks.Security;
using StackExchange.Redis;

namespace Reliquary.Accounts.Quotas;

/// <summary>
/// The result of the count of one request.
/// </summary>
/// <param name="Allowed">Whether the request can continue.</param>
/// <param name="Limit">The limit that applies.</param>
/// <param name="Remaining">The number of requests that remain today.</param>
/// <param name="Reset">The time until the counters start again from zero.</param>
internal sealed record QuotaResult(bool Allowed, long Limit, long Remaining, TimeSpan Reset);

/// <summary>
/// Counts the requests of the API keys in Redis, so all the API replicas share the counters.
/// </summary>
/// <remarks>
/// Each day (UTC) has a counter for each API key and a counter for each account.
/// One Lua script checks and increases the two counters, so two replicas cannot both use the last request.
/// </remarks>
internal sealed class QuotaCounter(Lazy<IConnectionMultiplexer> redis, IOptions<QuotaOptions> options, TimeProvider time)
{
    /// <summary>The start of all the Redis keys of the counters.</summary>
    public const string KeyPrefix = "reliquary:usage:";

    // The counters stay three days, so the flush job can read the previous day.
    private const int CounterLifetimeSeconds = 3 * 24 * 3600;

    private const string Script = """
        local key_count = tonumber(redis.call('GET', KEYS[1]) or '0')
        local account_count = tonumber(redis.call('GET', KEYS[2]) or '0')
        if key_count >= tonumber(ARGV[1]) or account_count >= tonumber(ARGV[2]) then
          return {0, key_count, account_count}
        end
        key_count = redis.call('INCR', KEYS[1])
        account_count = redis.call('INCR', KEYS[2])
        redis.call('EXPIRE', KEYS[1], ARGV[3])
        redis.call('EXPIRE', KEYS[2], ARGV[3])
        redis.call('SADD', KEYS[3], ARGV[4])
        redis.call('SADD', KEYS[4], ARGV[5])
        redis.call('EXPIRE', KEYS[3], ARGV[3])
        redis.call('EXPIRE', KEYS[4], ARGV[3])
        return {1, key_count, account_count}
        """;

    public static string Day(DateTimeOffset now) => now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string CounterKey(string day, string kind, Guid id) => $"{KeyPrefix}{day}:{kind}:{id:N}";

    public static string SubjectsKey(string day, string kind) => $"{KeyPrefix}{day}:{kind}s";

    public async Task<QuotaResult> CountAsync(Guid keyId, Guid accountId)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var day = Day(now);
        var reset = now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime;
        var result = (RedisResult[])(await redis.Value.GetDatabase().ScriptEvaluateAsync(
            Script,
            [CounterKey(day, "key", keyId), CounterKey(day, "account", accountId), SubjectsKey(day, "key"), SubjectsKey(day, "account")],
            [settings.RequestsPerKeyPerDay, settings.RequestsPerAccountPerDay, CounterLifetimeSeconds, keyId.ToString("N"), accountId.ToString("N")]))!;

        var allowed = (long)result[0] == 1;
        var keyCount = (long)result[1];
        var accountCount = (long)result[2];
        var keyRemaining = settings.RequestsPerKeyPerDay - keyCount;
        var accountRemaining = settings.RequestsPerAccountPerDay - accountCount;
        var limit = keyRemaining <= accountRemaining ? settings.RequestsPerKeyPerDay : settings.RequestsPerAccountPerDay;
        return new QuotaResult(allowed, limit, Math.Max(0, Math.Min(keyRemaining, accountRemaining)), reset);
    }
}

/// <summary>
/// Applies the daily quotas to the requests with an API key, on all the endpoints of the public API.
/// It also refuses a request with a credential that is not valid, also on a public endpoint.
/// </summary>
internal sealed class QuotaFilter(QuotaCounter counter, ILogger<QuotaFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        // The authentication middleware keeps the result only when it succeeds, so the filter checks a credential that did not authenticate the user.
        if (http.Request.Headers.Authorization.Count > 0
            && http.User.Identity?.IsAuthenticated != true
            && (await http.AuthenticateAsync()).Failure is not null)
        {
            http.Response.Headers.WWWAuthenticate = "Bearer";
            return ApiProblems.Problem(
                StatusCodes.Status401Unauthorized,
                AccountErrorCodes.InvalidCredentialsSent,
                "The access token or the API key is not valid. Send a valid credential, or send no Authorization header.");
        }

        var user = http.User;
        if (user.FindFirst(ReliquaryClaims.AuthMethod)?.Value != AuthMethods.ApiKey
            || !Guid.TryParse(user.FindFirst(ReliquaryClaims.ApiKeyId)?.Value, out var keyId))
        {
            return await next(context);
        }

        QuotaResult result;
        try
        {
            result = await counter.CountAsync(keyId, user.GetAccountId());
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or InvalidOperationException)
        {
            // A failure of Redis must not stop the API. The request continues without a count.
            logger.QuotaUnavailable(exception, keyId);
            return await next(context);
        }

        var headers = http.Response.Headers;
        var resetSeconds = ((long)Math.Ceiling(result.Reset.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Limit"] = result.Limit.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Remaining"] = result.Remaining.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Reset"] = resetSeconds;
        if (!result.Allowed)
        {
            headers.RetryAfter = resetSeconds;
            return ApiProblems.Problem(
                StatusCodes.Status429TooManyRequests,
                ErrorCodes.QuotaExceeded,
                "The API key or the account used all its requests of the day. Wait until 00:00 UTC, then try again.");
        }

        return await next(context);
    }
}

/// <summary>
/// Copies the request counters of Redis to the table <c>users.usage_counters</c>, for the statistics and the history.
/// </summary>
internal sealed class UsageFlushJob(IConnectionMultiplexer redis, AccountStore store, TimeProvider time, ILogger<UsageFlushJob> logger) : IScheduledJob
{
    /// <inheritdoc />
    public string Name => "accounts.usage-flush";

    /// <inheritdoc />
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public async Task RunAsync(JobRun run, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var database = redis.GetDatabase();
        var rows = new List<UsageRow>();
        foreach (var date in new[] { now.AddDays(-1), now })
        {
            var day = QuotaCounter.Day(date);
            foreach (var kind in new[] { "key", "account" })
            {
                var members = await database.SetMembersAsync(QuotaCounter.SubjectsKey(day, kind));
                if (members.Length == 0)
                {
                    continue;
                }

                var ids = members.Select(member => Guid.ParseExact(member.ToString(), "N")).ToArray();
                var values = await database.StringGetAsync(ids.Select(id => (RedisKey)QuotaCounter.CounterKey(day, kind, id)).ToArray());
                for (var index = 0; index < ids.Length; index++)
                {
                    if (values[index].TryParse(out long count))
                    {
                        rows.Add(new UsageRow { Day = day, SubjectKind = kind, SubjectId = ids[index], RequestCount = count });
                    }
                }
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into users.usage_counters (day, subject_kind, subject_id, request_count)
            values (@Day::date, @SubjectKind, @SubjectId, @RequestCount)
            on conflict (day, subject_kind, subject_id)
            do update set request_count = greatest(users.usage_counters.request_count, excluded.request_count)
            """,
            rows,
            cancellationToken: cancellationToken));
        logger.UsageFlushed(rows.Count);
    }

    private sealed class UsageRow
    {
        public string Day { get; set; } = string.Empty;

        public string SubjectKind { get; set; } = string.Empty;

        public Guid SubjectId { get; set; }

        public long RequestCount { get; set; }
    }
}
