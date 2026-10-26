using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Reliquary.Api.Tests;

/// <summary>
/// Starts the API host for the tests, in the environment "Testing".
/// The host does not read appsettings.Development.json, so it does not connect to a local database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
