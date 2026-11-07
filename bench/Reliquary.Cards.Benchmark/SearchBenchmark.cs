using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Reliquary.Cards.Benchmark;

/// <summary>
/// Measures the response time of the card search through the full API pipeline, in process.
/// </summary>
/// <remarks>
/// <para>Usage: <c>dotnet run --project bench/Reliquary.Cards.Benchmark -c Release -- [options]</c>.</para>
/// <list type="bullet">
/// <item><c>--connection</c>: the connection string of a database with an import of the card data.
/// The default is the variable <c>RELIQUARY_BENCH_DATABASE</c>.</item>
/// <item><c>--iterations</c>: the number of measured requests for each query. The default is 50.</item>
/// <item><c>--warmup</c>: the number of requests for each query before the measure. The default is 5.</item>
/// <item><c>--output</c>: a file for the Markdown table of the results.</item>
/// <item><c>--threshold</c>: the limit of the 95th percentile of all the requests, in milliseconds. The default is 150.</item>
/// </list>
/// The program exits with the code 1 when the 95th percentile is equal to or more than the limit,
/// and with the code 2 when a request fails.
/// </remarks>
public static class SearchBenchmark
{
    /// <summary>The typical queries of the benchmark: simple and complex queries of each kind.</summary>
    public static readonly IReadOnlyList<(string Kind, string Query)> Queries =
    [
        ("Name", "bolt"),
        ("Name", "lightning bolt"),
        ("Name", "!\"Sol Ring\""),
        ("Name", "jace"),
        ("Color", "c:rg"),
        ("Color", "id<=esper t:instant"),
        ("Color", "c=2 is:bear"),
        ("Type", "t:creature"),
        ("Type", "t:legendary t:goblin"),
        ("Text", "o:draw t:creature"),
        ("Text", "o:\"~ enters tapped\""),
        ("Text", "kw:flying -t:creature"),
        ("Mana", "m:2WW"),
        ("Mana", "mv>=7 -t:land"),
        ("Mana", "devotion:{u/b}{u/b}{u/b}"),
        ("Stats", "pow>tou c:w t:creature"),
        ("Legality", "f:commander c:g t:creature"),
        ("Legality", "banned:legacy"),
        ("Legality", "is:commander id:wubrg"),
        ("Tags", "function:removal"),
        ("Tags", "otag:ramp c:g mv<=3"),
        ("Tags", "art:squirrel"),
        ("Printing", "e:war"),
        ("Printing", "e:war is:booster r>=r"),
        ("Printing", "usd>=0.50 e:ema"),
        ("Printing", "a:avon t:land"),
        ("Printing", "game:arena year>=2024 c:u"),
        ("Boolean", "t:legendary (t:goblin or t:elf) -c:r"),
        ("Boolean", "(o:draw or o:scry) mv<=2 id<=ub -t:land"),
        ("Boolean", "t:fish or t:bird or t:bat"),
        ("Regex", "t:creature o:/^{T}:/"),
        ("Regex", "name:/\\bizzet\\b/"),
        ("Regex", "t:instant o:/\\spp/"),
        ("Display", "t:human order:cmc dir:desc"),
        ("Display", "!\"Lightning Bolt\" unique:prints"),
        ("Display", "t:forest a:avon unique:art"),
    ];

    /// <summary>
    /// Runs the benchmark.
    /// </summary>
    /// <param name="args">The command line options.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Connection is null)
        {
            await Console.Error.WriteLineAsync("Give the connection string with --connection or with the variable RELIQUARY_BENCH_DATABASE.");
            return 2;
        }

        var environment = await DescribeEnvironmentAsync(options.Connection);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("src/Reliquary.Api", "Reliquary.slnx");
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Users", options.Connection);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        });
        using var client = factory.CreateClient();

        var samples = Queries.ToDictionary(query => query.Query, _ => new List<double>());
        var totals = new Dictionary<string, long>();
        foreach (var (_, query) in Queries)
        {
            for (var index = 0; index < options.Warmup; index++)
            {
                var (_, total, error) = await MeasureAsync(client, query);
                if (error is not null)
                {
                    await Console.Error.WriteLineAsync($"The query \"{query}\" failed: {error}");
                    return 2;
                }

                totals[query] = total;
            }
        }

        // Each round runs all the queries, so that a query never runs many times in a row with the same cache.
        for (var round = 0; round < options.Iterations; round++)
        {
            foreach (var (_, query) in Queries)
            {
                var (milliseconds, _, error) = await MeasureAsync(client, query);
                if (error is not null)
                {
                    await Console.Error.WriteLineAsync($"The query \"{query}\" failed: {error}");
                    return 2;
                }

                samples[query].Add(milliseconds);
            }
        }

        var all = samples.Values.SelectMany(values => values).ToList();
        var overall = Percentile(all, 95);
        var report = Report(environment, options, samples, totals, all);
        Console.WriteLine(report);
        if (options.Output is not null)
        {
            await File.WriteAllTextAsync(options.Output, report);
        }

        if (overall >= options.Threshold)
        {
            await Console.Error.WriteLineAsync($"The 95th percentile is {overall:F1} ms. The limit is {options.Threshold} ms.");
            return 1;
        }

        return 0;
    }

    private static async Task<(double Milliseconds, long Total, string? Error)> MeasureAsync(HttpClient client, string query)
    {
        var uri = new Uri("/api/v1/cards/search?q=" + Uri.EscapeDataString(query), UriKind.Relative);
        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        stopwatch.Stop();
        if (!response.IsSuccessStatusCode)
        {
            return (0, 0, $"HTTP {(int)response.StatusCode}: {body}");
        }

        using var document = System.Text.Json.JsonDocument.Parse(body);
        return (stopwatch.Elapsed.TotalMilliseconds, document.RootElement.GetProperty("total").GetInt64(), null);
    }

    /// <summary>
    /// Gives a percentile with the nearest-rank method.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <param name="percentile">The percentile, from 1 to 100.</param>
    /// <returns>The value of the percentile.</returns>
    public static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var rank = (int)Math.Ceiling(percentile / 100 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    private static string Report(string environment, Options options, Dictionary<string, List<double>> samples, Dictionary<string, long> totals, List<double> all)
    {
        var culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("## Card search benchmark");
        builder.AppendLine();
        builder.Append(environment);
        builder.AppendLine(culture, $"- Requests: {options.Warmup} for the warm-up, then {options.Iterations} for each query, {all.Count} in all");
        builder.AppendLine();
        builder.AppendLine("| Kind | Query | Results | p50 (ms) | p95 (ms) | p99 (ms) | Max (ms) |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (kind, query) in Queries)
        {
            var values = samples[query];
            builder.AppendLine(culture, $"| {kind} | `{query.Replace("|", "\\|", StringComparison.Ordinal)}` | {totals[query]} | {Percentile(values, 50):F1} | {Percentile(values, 95):F1} | {Percentile(values, 99):F1} | {values.Max():F1} |");
        }

        builder.AppendLine(culture, $"| All | All the queries | | {Percentile(all, 50):F1} | **{Percentile(all, 95):F1}** | {Percentile(all, 99):F1} | {all.Max():F1} |");
        builder.AppendLine();
        var overall = Percentile(all, 95);
        builder.AppendLine(culture, $"Result: the 95th percentile is {overall:F1} ms. The limit is {options.Threshold} ms. The benchmark {(overall < options.Threshold ? "passes" : "fails")}.");
        return builder.ToString();
    }

    private static async Task<string> DescribeEnvironmentAsync(string connectionString)
    {
        var builder = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        builder.AppendLine(culture, $"- Processor: {ProcessorName()}, {Environment.ProcessorCount} logical processors");
        builder.AppendLine(culture, $"- Memory: {MemoryGigabytes()}");
        builder.AppendLine(culture, $"- Operating system: {RuntimeInformation.OSDescription}");
        builder.AppendLine(culture, $"- .NET: {RuntimeInformation.FrameworkDescription}");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select current_setting('server_version'), current_setting('shared_buffers'), current_setting('work_mem'),
                   (select count(*) from reference.oracle_cards), (select count(*) from reference.printings)
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        builder.AppendLine(culture, $"- PostgreSQL: {reader.GetString(0)}, shared_buffers {reader.GetString(1)}, work_mem {reader.GetString(2)}");
        builder.AppendLine(culture, $"- Card data: {reader.GetInt64(3)} Oracle cards, {reader.GetInt64(4)} printings");
        return builder.ToString();
    }

    private static string ProcessorName()
    {
        const string CpuInfo = "/proc/cpuinfo";
        if (File.Exists(CpuInfo))
        {
            var line = File.ReadLines(CpuInfo).FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal));
            if (line is not null)
            {
                return line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
            }
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    private static string MemoryGigabytes()
    {
        const string MemInfo = "/proc/meminfo";
        if (File.Exists(MemInfo))
        {
            var line = File.ReadLines(MemInfo).FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.Ordinal));
            var digits = new string([.. (line ?? string.Empty).Where(char.IsAsciiDigit)]);
            if (long.TryParse(digits, CultureInfo.InvariantCulture, out var kilobytes))
            {
                return string.Create(CultureInfo.InvariantCulture, $"{kilobytes / 1024.0 / 1024.0:F1} GiB");
            }
        }

        return "unknown";
    }

    private sealed record Options(string? Connection, int Iterations, int Warmup, string? Output, double Threshold)
    {
        public static Options Parse(string[] args)
        {
            string? Value(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            return new Options(
                Value("--connection") ?? Environment.GetEnvironmentVariable("RELIQUARY_BENCH_DATABASE"),
                int.Parse(Value("--iterations") ?? "50", CultureInfo.InvariantCulture),
                int.Parse(Value("--warmup") ?? "5", CultureInfo.InvariantCulture),
                Value("--output"),
                double.Parse(Value("--threshold") ?? "150", CultureInfo.InvariantCulture));
        }
    }
}
