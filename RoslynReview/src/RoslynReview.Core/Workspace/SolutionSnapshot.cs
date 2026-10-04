using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynReview.Core.Workspace;

/// <summary>An immutable view of the solution under review together with the repository layout.</summary>
public sealed class SolutionSnapshot(Solution solution, RepositoryPaths paths)
{
    public Solution Solution { get; } = solution;

    public RepositoryPaths Paths { get; } = paths;

    /// <summary>Finds the documents for a repository-relative path such as <c>src/App/Program.cs</c>.</summary>
    /// <remarks>A file that is compiled into several projects (multi-targeting, linked files) has several documents.</remarks>
    public ImmutableArray<DocumentId> FindDocuments(string relativePath)
    {
        var exact = Solution.GetDocumentIdsWithFilePath(Paths.ToAbsolute(relativePath));
        if (!exact.IsEmpty)
        {
            return exact;
        }

        // Fallback for diffs made relative to a subdirectory (git diff --relative):
        // match whole path segments at the end of each document path.
        var suffix = "/" + RepositoryPaths.Normalize(relativePath).TrimStart('/');
        return [.. Solution.Projects
            .SelectMany(project => project.Documents)
            .Where(document => document.FilePath is { } path &&
                RepositoryPaths.Normalize(path).EndsWith(suffix, RepositoryPaths.Comparison))
            .Select(document => document.Id)];
    }

    public string RelativePath(Document document) =>
        document.FilePath is { } path ? Paths.ToRelative(path) : document.Name;
}
