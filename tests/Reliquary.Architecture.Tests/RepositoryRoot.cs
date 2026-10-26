namespace Reliquary.Architecture.Tests;

/// <summary>
/// Finds the root folder of the repository from the folder of the test assembly.
/// </summary>
internal static class RepositoryRoot
{
    private const string SolutionFile = "Reliquary.slnx";

    /// <summary>
    /// Gets the full path of the repository root: the folder that contains the solution file.
    /// </summary>
    public static string Path { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"The test cannot find {SolutionFile} in a parent folder of {AppContext.BaseDirectory}.");
    }
}
