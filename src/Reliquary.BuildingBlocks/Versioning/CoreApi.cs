namespace Reliquary.BuildingBlocks.Versioning;

/// <summary>
/// The version of the API that the core gives to extensions.
/// </summary>
/// <remarks>
/// An extension declares the versions that it supports in the <c>coreApi</c> field of its manifest, for example <c>^1.0</c>.
/// Increase the minor version for a compatible addition, and the major version for a change that breaks extensions.
/// The major version is also the version in the path of the internal API: <c>/internal/v1</c>.
/// Version 1.1.0 added the internal API for the modules. See docs/decisions/0036-internal-api.md.
/// </remarks>
public static class CoreApi
{
    /// <summary>Gets the current version of the core API.</summary>
    public static SemanticVersion Version { get; } = new(1, 1, 0);
}
