using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Accounts;
using Reliquary.BuildingBlocks.Api;

namespace Reliquary.Accounts.Profile;

/// <summary>The public profile of a user: the data that all the users can see.</summary>
/// <param name="Id">The ID of the account. The owner of the user data has the same ID.</param>
/// <param name="DisplayName">The display name.</param>
public sealed record PublicProfileResponse(Guid Id, string DisplayName);

/// <summary>
/// The endpoints of the internal API for the accounts.
/// </summary>
internal static class InternalProfileEndpoints
{
    public static void Map(IEndpointRouteBuilder internalApi)
    {
        internalApi.MapGet("/users/{accountId:guid}/profile", GetProfileAsync)
            .WithTags("Users")
            .WithName("GetPublicProfile")
            .WithSummary("Gets the public profile of a user. The response never contains the email address.");
    }

    private static async Task<Results<Ok<PublicProfileResponse>, ProblemHttpResult>> GetProfileAsync(Guid accountId, IAccountDirectory directory, CancellationToken cancellationToken)
    {
        var profiles = await directory.GetProfilesAsync([accountId], cancellationToken);
        return profiles.TryGetValue(accountId, out var profile)
            ? TypedResults.Ok(new PublicProfileResponse(profile.AccountId, profile.DisplayName))
            : ApiProblems.NotFound("The user does not exist.");
    }
}
