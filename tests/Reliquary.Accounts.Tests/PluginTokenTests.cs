using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Reliquary.Accounts.Plugins;
using Reliquary.Accounts.ServiceAccounts;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.Tests;

/// <summary>
/// The plugin tokens. See docs/decisions/0049-plugin-sandbox.md.
/// </summary>
public sealed class PluginTokenTests
{
    private const string PluginId = "deck-reader";

    private const string TokenPath = $"/api/v1/me/extensions/{PluginId}/token";

    private static readonly string[] DecksRead = [Scopes.DecksRead];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PluginTokenHasOnlyTheScopesOfThePlugin()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.InstallAsync(activate: true);

        var created = await setup.User.PostAsync(TokenPath);
        var token = (await created.Content.ReadFromJsonAsync<PluginTokenResponse>(Cancel))!;
        var (header, payload) = Decode(token.Token);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.StartsWith("rlp_", token.Token, StringComparison.Ordinal);
        Assert.Equal(DecksRead, token.Scopes);
        Assert.InRange(token.ExpiresAt - DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("iat").GetInt64()), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64()), DateTimeOffset.FromUnixTimeSeconds(token.ExpiresAt.ToUnixTimeSeconds()));
        Assert.Equal("rlp+jwt", header.GetProperty("typ").GetString());
        Assert.Equal("reliquary-plugin", payload.GetProperty("aud").GetString());
        Assert.Equal(setup.AccountId.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal(PluginId, payload.GetProperty("plg").GetString());
        Assert.Equal("decks:read", payload.GetProperty("scope").GetString());
        Assert.Equal("plugin", payload.GetProperty("amr").GetString());

        // An endpoint with the scope of the token accepts it.
        Assert.Equal(HttpStatusCode.OK, (await setup.User.GetAsync("/api/v1/decks", token.Token)).StatusCode);

        // An endpoint with another scope, an endpoint without a scope and a filter that needs another scope refuse it.
        await AssertScopeMissingAsync(await setup.User.GetAsync("/api/v1/me", token.Token));
        await AssertScopeMissingAsync(await setup.User.GetAsync("/api/v1/status", token.Token));
        await AssertScopeMissingAsync(await setup.User.GetAsync("/api/v1/decks?complete=true", token.Token));

        // The endpoints of the web sessions and of the administrators refuse it.
        Assert.Equal(HttpStatusCode.Forbidden, (await setup.User.GetAsync("/api/v1/me/api-keys", token.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await setup.User.SendAsync(HttpMethod.Post, TokenPath, new { }, token.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await setup.User.SendAsync(HttpMethod.Put, $"/api/v1/me/extensions/{PluginId}", new { enabled = false }, token.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await setup.User.GetAsync("/api/v1/admin/extensions", token.Token)).StatusCode);
    }

    [Fact]
    public async Task OnlyAWebSessionGetsATokenForAnActivePlugin()
    {
        await using var setup = await Setup.CreateAsync();

        var notInstalled = await setup.User.PostAsync(TokenPath);
        await setup.InstallAsync(activate: false);
        var notActivated = await setup.User.PostAsync(TokenPath);
        await setup.ActivateAsync(true);
        var key = (await (await setup.User.PostAsync("/api/v1/me/api-keys", new { name = "Script" })).Content.ReadFromJsonAsync<Profile.CreatedApiKeyResponse>(Cancel))!;
        var withKey = await setup.User.SendAsync(HttpMethod.Post, TokenPath, new { }, key.Key);
        using var stranger = setup.Api.CreateBrowser();
        var anonymous = await stranger.PostAsync(TokenPath);
        var created = await setup.User.PostAsync(TokenPath);

        Assert.Equal(HttpStatusCode.NotFound, notInstalled.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notActivated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    [Fact]
    public async Task DeactivationStopsTheTokenImmediately()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.InstallAsync(activate: true);
        var token = (await (await setup.User.PostAsync(TokenPath)).Content.ReadFromJsonAsync<PluginTokenResponse>(Cancel))!.Token;

        await setup.ActivateAsync(false);
        var afterUser = await setup.User.GetAsync("/api/v1/decks", token);
        await setup.ActivateAsync(true);
        var afterReactivation = await setup.User.GetAsync("/api/v1/decks", token);
        (await setup.Admin.SendAsync(HttpMethod.Patch, $"/api/v1/admin/extensions/{PluginId}", new { enabled = false })).EnsureSuccessStatusCode();
        var afterAdministrator = await setup.User.GetAsync("/api/v1/decks", token);

        Assert.Equal(HttpStatusCode.Unauthorized, afterUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterReactivation.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterAdministrator.StatusCode);
    }

    [Fact]
    public async Task PluginTokenIsNeverAnAccessTokenOrADelegationToken()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.InstallAsync(activate: true);
        var pluginToken = (await (await setup.User.PostAsync(TokenPath)).Content.ReadFromJsonAsync<PluginTokenResponse>(Cancel))!.Token;
        var jwt = pluginToken["rlp_".Length..];

        // A plugin token without its start goes to the scheme of the access tokens, which refuses its type, its audience and its method.
        Assert.Equal(HttpStatusCode.Unauthorized, (await setup.User.GetAsync("/api/v1/decks", jwt)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await setup.User.GetAsync("/api/v1/me/api-keys", jwt)).StatusCode);

        // A plugin token with the start of a delegation token is not a delegation token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await setup.User.GetAsync("/api/v1/decks", "rld_" + jwt)).StatusCode);

        // Tokens signed with the key of the instance, with one property of the other kind of token.
        string[] refused =
        [
            Sign(type: "JWT", audience: "reliquary", method: AuthMethods.Plugin),
            Sign(type: PluginTokens.TokenType, audience: "reliquary", method: AuthMethods.Session),
            "rlp_" + Sign(type: "JWT", audience: PluginTokens.Audience, method: AuthMethods.Plugin),
            "rlp_" + Sign(type: PluginTokens.TokenType, audience: "reliquary", method: AuthMethods.Plugin),
            "rlp_" + Sign(type: PluginTokens.TokenType, audience: PluginTokens.Audience, method: AuthMethods.Session),
            "rlp_" + Sign(type: PluginTokens.TokenType, audience: PluginTokens.Audience, method: AuthMethods.Plugin, expired: true),
            "rlp_" + Sign(type: PluginTokens.TokenType, audience: PluginTokens.Audience, method: AuthMethods.Plugin, key: "b3RoZXItc2lnbmluZy1rZXktb3RoZXItc2lnbmluZy1rZXk="),
        ];
        foreach (var token in refused)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await setup.User.GetAsync("/api/v1/decks", token)).StatusCode);
        }

        // The control: the same helper with all the properties of a plugin token gives a valid token.
        Assert.Equal(HttpStatusCode.OK, (await setup.User.GetAsync("/api/v1/decks", "rlp_" + Sign(PluginTokens.TokenType, PluginTokens.Audience, AuthMethods.Plugin, accountId: setup.AccountId))).StatusCode);
    }

    [Fact]
    public async Task AccessTokenAndDelegationTokenAreNeverPluginTokens()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.InstallAsync(activate: true);
        var delegationToken = await setup.DelegationTokenAsync();

        // An access token with the start of a plugin token goes to the plugin scheme, which refuses it.
        var accessAsPlugin = await setup.User.GetAsync("/api/v1/decks", "rlp_" + setup.User.AccessToken);

        // A delegation token is a random secret, so it is never a plugin token.
        var delegationAsPlugin = await setup.User.GetAsync("/api/v1/decks", "rlp_" + delegationToken);
        var delegationAsPluginWithoutStart = await setup.User.GetAsync("/api/v1/decks", "rlp_" + delegationToken["rld_".Length..]);

        // A delegation token cannot get a plugin token, because only a web session can.
        var delegationGetsToken = await setup.User.SendAsync(HttpMethod.Post, TokenPath, new { }, delegationToken);

        // The control: the delegation token works with its own scope.
        var delegationWorks = await setup.User.GetAsync("/api/v1/decks", delegationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, accessAsPlugin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delegationAsPlugin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delegationAsPluginWithoutStart.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delegationGetsToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delegationWorks.StatusCode);
    }

    private static async Task AssertScopeMissingAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrorCodes.ScopeMissing, await response.CodeAsync());
    }

    private static (JsonElement Header, JsonElement Payload) Decode(string token)
    {
        var parts = token["rlp_".Length..].Split('.');
        return (JsonElement.Parse(Base64UrlEncoder.Decode(parts[0])), JsonElement.Parse(Base64UrlEncoder.Decode(parts[1])));
    }

    /// <summary>Signs a JWT with the given properties, as an attacker with a token of another kind could try.</summary>
    private static string Sign(string type, string audience, string method, bool expired = false, string key = AccountsInstance.SigningKey, Guid? accountId = null)
    {
        var now = DateTime.UtcNow;
        var start = expired ? now.AddHours(-2) : now;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "reliquary",
            Audience = audience,
            TokenType = type,
            Claims = new Dictionary<string, object>
            {
                [ReliquaryClaims.AccountId] = (accountId ?? Guid.NewGuid()).ToString(),
                [ReliquaryClaims.AuthMethod] = method,
                [ReliquaryClaims.PluginId] = PluginId,
                [ReliquaryClaims.Scope] = Scopes.DecksRead,
            },
            IssuedAt = start,
            NotBefore = start,
            Expires = start.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(key)), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>An instance with an administrator and a user.</summary>
    private sealed class Setup : IAsyncDisposable
    {
        private Setup(AccountsInstance instance, AccountsApi api, BrowserClient admin, BrowserClient user, Guid accountId)
        {
            Instance = instance;
            Api = api;
            Admin = admin;
            User = user;
            AccountId = accountId;
        }

        public AccountsInstance Instance { get; }

        public AccountsApi Api { get; }

        public BrowserClient Admin { get; }

        public BrowserClient User { get; }

        public Guid AccountId { get; }

        public static async Task<Setup> CreateAsync()
        {
            var instance = await AccountsInstance.CreateAsync();
            var api = instance.StartReplica();
            var admin = await ServiceAccountSetup.SignInAdministratorAsync(instance, api);
            var user = api.CreateBrowser();
            await AuthFlowTests.RegisterCheckedAsync(instance, user, "plugin-user@example.org");
            var session = await user.SignInAsync("plugin-user@example.org", AuthFlowTests.Password);
            return new Setup(instance, api, admin, user, session.Account.Id);
        }

        /// <summary>Installs and enables the plugin with the permission decks:read. The user activates it when <paramref name="activate"/> is true.</summary>
        public async Task InstallAsync(bool activate)
        {
            var manifest = new { id = PluginId, name = "Deck reader", version = "1.0.0", coreApi = "^1.0", entry = "index.js", permissions = DecksRead };
            Assert.Equal(HttpStatusCode.Created, (await Admin.SendAsync(HttpMethod.Put, $"/api/v1/admin/extensions/{PluginId}", manifest)).StatusCode);
            (await Admin.SendAsync(HttpMethod.Patch, $"/api/v1/admin/extensions/{PluginId}", new { enabled = true })).EnsureSuccessStatusCode();
            if (activate)
            {
                await ActivateAsync(true);
            }
        }

        public async Task ActivateAsync(bool enabled) =>
            Assert.Equal(HttpStatusCode.NoContent, (await User.SendAsync(HttpMethod.Put, $"/api/v1/me/extensions/{PluginId}", new { enabled, consent = new { version = "1.0.0", scopes = DecksRead } })).StatusCode);

        /// <summary>Gets a delegation token with the scope decks:read for the user.</summary>
        public async Task<string> DelegationTokenAsync()
        {
            var secret = await ServiceAccountSetup.CreateServiceAccountAsync(Api, "module");
            var serviceAccountId = await ServiceAccountSetup.ServiceAccountIdAsync(Api, "module");
            var delegation = (await (await User.PostAsync("/api/v1/me/delegations", new { serviceAccountId, scopes = DecksRead, expiresInDays = 30 }))
                .Content.ReadFromJsonAsync<DelegationResponse>(Cancel))!;
            var response = await User.SendAsync(HttpMethod.Post, $"/internal/v1/delegations/{delegation.Id}/tokens", new { scopes = DecksRead }, secret);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<DelegationTokenResponse>(Cancel))!.Token;
        }

        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            User.Dispose();
            await Instance.DisposeAsync();
        }
    }
}
