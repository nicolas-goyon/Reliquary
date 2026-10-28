using System.Security.Claims;
using Microsoft.AspNetCore.Builder;

namespace Reliquary.BuildingBlocks.Security;

/// <summary>
/// The scopes of the delegation tokens. A scope gives access to one group of endpoints of the public API.
/// See docs/architecture/service-accounts.md.
/// </summary>
public static class Scopes
{
    /// <summary>Read the decks, the deck folders and the deck tags of the user.</summary>
    public const string DecksRead = "decks:read";

    /// <summary>Create, change and delete the decks, the deck folders and the deck tags of the user.</summary>
    public const string DecksWrite = "decks:write";

    /// <summary>Read the collection of the user.</summary>
    public const string CollectionRead = "collection:read";

    /// <summary>Change the collection of the user.</summary>
    public const string CollectionWrite = "collection:write";

    /// <summary>Read the account of the user: the email address, the display name and the language.</summary>
    public const string ProfileRead = "profile:read";

    /// <summary>Gets all the scopes, in the order of the documentation.</summary>
    public static IReadOnlyList<string> All { get; } = [DecksRead, DecksWrite, CollectionRead, CollectionWrite, ProfileRead];
}

/// <summary>
/// The scope that an endpoint needs from a delegation token. An endpoint without this metadata refuses the delegation tokens.
/// </summary>
/// <param name="Scope">The scope, for example <c>decks:read</c>.</param>
public sealed record RequiredScopeMetadata(string Scope);

/// <summary>
/// Declares the scope of an endpoint for the delegation tokens, and reads the scopes of a principal.
/// </summary>
public static class ScopeEndpointExtensions
{
    /// <summary>
    /// Lets a delegation token with the scope use the endpoint.
    /// The other credentials do not need a scope.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the endpoint builder.</typeparam>
    /// <param name="builder">The endpoint or the group of endpoints.</param>
    /// <param name="scope">The scope. See <see cref="Scopes"/>.</param>
    /// <returns>The builder.</returns>
    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Scopes.All.Contains(scope, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The scope \"{scope}\" does not exist. Use a value of {nameof(Scopes)}.", nameof(scope));
        }

        builder.WithMetadata(new RequiredScopeMetadata(scope));
        return builder;
    }

    /// <summary>
    /// Tells whether a principal uses a delegation token.
    /// </summary>
    /// <param name="user">The principal.</param>
    /// <returns><see langword="true"/> for a delegation token.</returns>
    public static bool IsDelegated(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Identity?.IsAuthenticated == true && user.FindFirst(ReliquaryClaims.AuthMethod)?.Value == AuthMethods.Delegation;
    }

    /// <summary>
    /// Tells whether a delegation token has a scope.
    /// </summary>
    /// <param name="user">The principal.</param>
    /// <param name="scope">The scope.</param>
    /// <returns><see langword="true"/> when the principal has a claim for the scope.</returns>
    public static bool HasScope(this ClaimsPrincipal user, string scope)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindAll(ReliquaryClaims.Scope).Any(claim => claim.Value == scope);
    }
}
