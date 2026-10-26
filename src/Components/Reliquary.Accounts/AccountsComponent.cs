using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reliquary.BuildingBlocks;

namespace Reliquary.Accounts;

/// <summary>
/// Registers the Accounts core component: accounts, authentication, API keys and quotas.
/// </summary>
public sealed class AccountsComponent : ICoreComponent
{
    /// <inheritdoc />
    public string Name => "Accounts";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
