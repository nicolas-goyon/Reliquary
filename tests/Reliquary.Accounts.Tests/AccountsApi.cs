using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Reliquary.Accounts.Auth;
using Reliquary.BuildingBlocks.Email;
using Reliquary.Platform.Migrations;
using Reliquary.Testing;
using StackExchange.Redis;

namespace Reliquary.Accounts.Tests;

/// <summary>
/// Keeps the emails of the tests in memory.
/// </summary>
public sealed partial class EmailOutbox : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> messages = new();

    public IReadOnlyList<EmailMessage> Messages => messages.ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        messages.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the token of the last link in the emails to an address.
    /// </summary>
    public string LastToken(string to)
    {
        var message = messages.Last(message => message.To == to);
        return Uri.UnescapeDataString(TokenPattern().Match(message.TextBody).Groups[1].Value);
    }

    [GeneratedRegex(@"\?token=([^\s]+)")]
    private static partial Regex TokenPattern();
}

/// <summary>
/// The shared data of a group of API replicas: one database, one Redis server, one email outbox and one clock.
/// </summary>
public sealed class AccountsInstance : IAsyncDisposable
{
    public const string SigningKey = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGhlLWFjY291bnRzLXRlc3Rz";

    private readonly List<AccountsApi> replicas = [];

    private AccountsInstance(TestDatabase database) => Database = database;

    public TestDatabase Database { get; }

    public EmailOutbox Emails { get; } = new();

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public Dictionary<string, string?> Settings { get; } = [];

    public static async Task<AccountsInstance> CreateAsync()
    {
        try
        {
            await using var redis = await ConnectionMultiplexer.ConnectAsync(TestServers.Redis);
            await redis.GetDatabase().PingAsync();
        }
        catch (RedisException exception)
        {
            TestServers.Unavailable("Redis", exception);
        }

        return new AccountsInstance(await TestServers.CreateDatabaseAsync("accounts"));
    }

    /// <summary>
    /// Starts a new replica of the API. All the replicas share the database, Redis, the emails and the clock.
    /// </summary>
    public AccountsApi StartReplica()
    {
        var replica = new AccountsApi(this);
        replicas.Add(replica);
        if (replicas.Count == 1)
        {
            // The first replica applies the migrations.
            using var scope = replica.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<MigrationRunner>().MigrateAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }

        return replica;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var replica in replicas)
        {
            await replica.DisposeAsync();
        }

        await Database.DisposeAsync();
    }
}

/// <summary>
/// One replica of the API host.
/// </summary>
public sealed class AccountsApi(AccountsInstance instance) : WebApplicationFactory<Program>
{
    public AccountsInstance Instance { get; } = instance;

    /// <summary>
    /// Creates a client that keeps the refresh cookie, as a browser does.
    /// </summary>
    public BrowserClient CreateBrowser() => new(CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Users", Instance.Database.ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", TestServers.Redis);
        builder.UseSetting("Auth:SigningKey", AccountsInstance.SigningKey);
        builder.UseSetting("Auth:PublicUrl", "https://reliquary.test");
        builder.UseSetting("Auth:RequestsPerMinute", "1000");
        builder.UseSetting("Auth:AdministratorEmails:0", "admin@example.org");
        foreach (var (key, value) in Instance.Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(Instance.Emails);
            services.AddSingleton<TimeProvider>(Instance.Time);
        });
    }
}

/// <summary>
/// An HTTP client that keeps the refresh cookie and the access token, as the web interface does.
/// </summary>
public sealed class BrowserClient(HttpClient client) : IDisposable
{
    public HttpClient Http { get; } = client;

    public string? RefreshCookie { get; set; }

    public string? AccessToken { get; set; }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (RefreshCookie is not null && path.StartsWith("/api/v1/auth", StringComparison.Ordinal))
        {
            request.Headers.Add("Cookie", $"{Auth.RefreshCookie.Name}={RefreshCookie}");
        }

        if ((bearer ?? AccessToken) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies.Where(cookie => cookie.StartsWith(Auth.RefreshCookie.Name + "=", StringComparison.Ordinal)))
            {
                var value = cookie[(Auth.RefreshCookie.Name.Length + 1)..].Split(';')[0];
                RefreshCookie = value.Length == 0 ? null : value;
            }
        }

        return response;
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body ?? new { });

    public Task<HttpResponseMessage> GetAsync(string path, string? bearer = null) => SendAsync(HttpMethod.Get, path, null, bearer);

    /// <summary>
    /// Signs in and keeps the access token.
    /// </summary>
    public async Task<SessionResponse> SignInAsync(string email, string password)
    {
        var response = await PostAsync("/api/v1/auth/sign-in", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(TestContext.Current.CancellationToken))!;
        AccessToken = session.AccessToken;
        return session;
    }

    public void Dispose() => Http.Dispose();
}

internal static class Problems
{
    public static async Task<string?> CodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
