using System.Reflection;
using Reliquary.Hosting;

namespace Reliquary.Api;

/// <summary>
/// The status of the API: its name, its version and its core components.
/// </summary>
/// <param name="Name">The name of the application.</param>
/// <param name="Version">The version of the API host.</param>
/// <param name="Components">The names of the registered core components.</param>
internal sealed record ApiStatus(string Name, string Version, IReadOnlyList<string> Components)
{
    /// <summary>
    /// Creates the status from the API assembly and the registered core components.
    /// </summary>
    /// <returns>The current status of the API.</returns>
    public static ApiStatus Create()
    {
        var version = typeof(ApiStatus).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        // The SDK adds the commit hash after a "+" sign. The status shows the version only.
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            version = version[..plusIndex];
        }

        return new ApiStatus("Reliquary", version, [.. CoreComponents.All.Select(component => component.Name)]);
    }
}
