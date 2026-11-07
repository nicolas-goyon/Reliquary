using System.Reflection;
using Reliquary.Hosting;

namespace Reliquary.Api;

/// <summary>
/// The status of the API: its name, its version and its core components.
/// </summary>
/// <param name="Name">The name of the application.</param>
/// <param name="Version">The version of the API host.</param>
/// <param name="Components">The names of the registered core components.</param>
/// <param name="CoreApi">The version of the core API for the extensions.</param>
/// <param name="Commit">The commit of the build, or null when the build has no commit.</param>
internal sealed record ApiStatus(string Name, string Version, IReadOnlyList<string> Components, string CoreApi, string? Commit)
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

        // The SDK adds the commit hash after a "+" sign. The status gives the version and the commit separately.
        string? commit = null;
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            commit = version[(plusIndex + 1)..];
            version = version[..plusIndex];
        }

        return new ApiStatus(
            "Reliquary",
            version,
            [.. CoreComponents.All.Select(component => component.Name)],
            BuildingBlocks.Versioning.CoreApi.Version.ToString(),
            string.IsNullOrEmpty(commit) ? null : commit);
    }
}
