namespace RoslynReview.Core.Workspace;

/// <summary>Converts between absolute file paths and the repository-relative paths used by git diffs.</summary>
public sealed class RepositoryPaths
{
    private RepositoryPaths(string root) => Root = root;

    /// <summary>Absolute path of the repository root, without a trailing separator.</summary>
    public string Root { get; }

    /// <summary>File name comparison that matches the platform's default file system.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static RepositoryPaths Create(string solutionPath, string? repositoryRoot = null)
    {
        var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var root = repositoryRoot is not null
            ? Path.GetFullPath(repositoryRoot)
            : FindGitRoot(solutionDirectory) ?? solutionDirectory;
        return new RepositoryPaths(Path.TrimEndingDirectorySeparator(root));
    }

    /// <summary>
    /// Returns <paramref name="absolutePath"/> relative to <see cref="Root"/> with forward slashes,
    /// or the normalized absolute path when the file lies outside the repository.
    /// </summary>
    public string ToRelative(string absolutePath)
    {
        var relative = Path.GetRelativePath(Root, absolutePath);
        var outside = Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith("../", StringComparison.Ordinal) || relative.StartsWith(@"..\", StringComparison.Ordinal);
        return Normalize(outside ? absolutePath : relative);
    }

    public string ToAbsolute(string relativePath) =>
        Path.GetFullPath(Path.Combine(Root, relativePath.TrimStart('/', '\\')));

    public static string Normalize(string path) => path.Replace('\\', '/');

    private static string? FindGitRoot(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var git = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return current.FullName;
            }
        }

        return null;
    }
}
