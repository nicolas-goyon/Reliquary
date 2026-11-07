using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reliquary.BuildingBlocks.Api;
using Reliquary.BuildingBlocks.Security;

namespace Reliquary.Accounts.ServiceAccounts;

/// <summary>
/// The endpoints of the service accounts and of the delegations.
/// See docs/architecture/service-accounts.md.
/// </summary>
internal static class ServiceAccountEndpoints
{
    /// <summary>
    /// Adds the endpoints of the users and of the administrator to the public API.
    /// </summary>
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/service-accounts", ListForUserAsync)
            .WithTags("Delegations")
            .WithName("ListServiceAccounts")
            .WithSummary("Lists the active service accounts of the instance. The user can give a delegation to each of them.")
            .RequireAuthorization(AuthPolicies.Session);

        var delegations = api.MapGroup("/me/delegations").WithTags("Delegations").RequireAuthorization(AuthPolicies.Session);

        delegations.MapGet(string.Empty, ListDelegationsAsync)
            .WithName("ListDelegations")
            .WithSummary("Lists the active delegations of the user.");

        delegations.MapPost(string.Empty, CreateDelegationAsync)
            .WithName("CreateDelegation")
            .WithSummary("Lets a service account act for the user, with some scopes, until a date.");

        delegations.MapDelete("/{id:guid}", RevokeDelegationAsync)
            .WithName("RevokeDelegation")
            .WithSummary("Revokes a delegation. The tokens of the delegation stop working immediately.");

        var admin = api.MapGroup("/admin/service-accounts").WithTags("Administration").RequireAuthorization(AuthPolicies.Administrator);

        admin.MapGet(string.Empty, ListForAdministratorAsync)
            .WithName("AdminListServiceAccounts")
            .WithSummary("Lists all the service accounts, also the revoked service accounts.");

        admin.MapPost(string.Empty, CreateServiceAccountAsync)
            .WithName("AdminCreateServiceAccount")
            .WithSummary("Creates a service account. The response contains the secret one time only.");

        admin.MapDelete("/{id:guid}", RevokeServiceAccountAsync)
            .WithName("AdminRevokeServiceAccount")
            .WithSummary("Revokes a service account and all its delegations. The secret and the delegation tokens stop working immediately.");
    }

    /// <summary>
    /// Adds the endpoints of the delegations to the internal API.
    /// </summary>
    public static void MapInternal(IEndpointRouteBuilder internalApi)
    {
        var delegations = internalApi.MapGroup("/delegations").WithTags("Delegations");

        delegations.MapGet(string.Empty, ListInternalDelegationsAsync)
            .WithName("ListServiceAccountDelegations")
            .WithSummary("Lists the active delegations that the users gave to the service account.");

        delegations.MapPost("/{id:guid}/tokens", IssueTokenAsync)
            .WithName("CreateDelegationToken")
            .WithSummary("Gives a short delegation token. The service account sends it to the public API to act for the user, with the scopes of the token.");
    }

    private static async Task<Ok<IReadOnlyList<ServiceAccountSummary>>> ListForUserAsync(DelegationService delegations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await delegations.ListServiceAccountsAsync(cancellationToken));

    private static async Task<Ok<IReadOnlyList<DelegationResponse>>> ListDelegationsAsync(ClaimsPrincipal user, DelegationService delegations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await delegations.ListForAccountAsync(user.GetAccountId(), cancellationToken));

    private static async Task<Results<Created<DelegationResponse>, ProblemHttpResult>> CreateDelegationAsync(CreateDelegationRequest request, ClaimsPrincipal user, DelegationService delegations, CancellationToken cancellationToken)
    {
        var result = await delegations.CreateAsync(user.GetAccountId(), request, cancellationToken);
        if (result.Errors.Count > 0)
        {
            return ApiProblems.Validation(result.Errors);
        }

        if (result.NotFound)
        {
            return ApiProblems.NotFound("The service account does not exist, or the administrator revoked it.");
        }

        if (result.LimitReached)
        {
            return ApiProblems.Conflict(AccountErrorCodes.DelegationLimit, $"The account has {DelegationService.MaxDelegations} active delegations. Revoke a delegation, then try again.");
        }

        return TypedResults.Created($"{ApiRoutes.Base}/me/delegations/{result.Value!.Id}", result.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeDelegationAsync(Guid id, ClaimsPrincipal user, DelegationService delegations, CancellationToken cancellationToken) =>
        await delegations.RevokeAsync(user.GetAccountId(), id, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.NotFound("The delegation does not exist, or it is revoked.");

    private static async Task<Ok<IReadOnlyList<ServiceAccountResponse>>> ListForAdministratorAsync(ServiceAccountService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(includeRevoked: true, cancellationToken));

    private static async Task<Results<Created<CreatedServiceAccountResponse>, ProblemHttpResult>> CreateServiceAccountAsync(CreateServiceAccountRequest request, ServiceAccountService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request.Name, request.Description, cancellationToken);
        if (result.Errors.Count > 0)
        {
            return ApiProblems.Validation(result.Errors);
        }

        if (result.NameTaken)
        {
            return ApiProblems.Conflict(AccountErrorCodes.ServiceAccountNameTaken, "A service account with this name exists. Use a different name.");
        }

        var created = result.Created!;
        return TypedResults.Created($"{ApiRoutes.Base}/admin/service-accounts/{created.ServiceAccount.Id}", created);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeServiceAccountAsync(Guid id, ServiceAccountService service, CancellationToken cancellationToken) =>
        await service.RevokeAsync(id, cancellationToken)
            ? TypedResults.NoContent()
            : ApiProblems.NotFound("The service account does not exist, or it is revoked.");

    private static async Task<Ok<IReadOnlyList<InternalDelegationResponse>>> ListInternalDelegationsAsync(ClaimsPrincipal serviceAccount, DelegationService delegations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await delegations.ListForServiceAccountAsync(ServiceAccountId(serviceAccount), cancellationToken));

    private static async Task<Results<Ok<DelegationTokenResponse>, ProblemHttpResult>> IssueTokenAsync(Guid id, CreateDelegationTokenRequest? request, ClaimsPrincipal serviceAccount, DelegationService delegations, CancellationToken cancellationToken)
    {
        var result = await delegations.IssueTokenAsync(ServiceAccountId(serviceAccount), id, request ?? new CreateDelegationTokenRequest(null), cancellationToken);
        if (result.NotFound)
        {
            return ApiProblems.NotFound("The delegation does not exist, it expired, or the user revoked it.");
        }

        if (result.Errors.Count > 0)
        {
            return ApiProblems.Validation(result.Errors);
        }

        return TypedResults.Ok(result.Value!);
    }

    private static Guid ServiceAccountId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirst(ReliquaryClaims.ServiceAccountId)?.Value
            ?? throw new InvalidOperationException("The principal is not a service account. Use the policy AuthPolicies.ServiceAccount on the endpoint."));
}
