using Microsoft.Extensions.Configuration;
using RoslynReview.Core.Workspace;

namespace RoslynReview.McpServer;

public sealed class ServerConfigurationException(string message) : Exception(message);

/// <summary>Startup settings. The solution is fixed for the lifetime of the server: tools never take file paths.</summary>
public static class ServerConfiguration
{
    /// <summary>Sent to the client once per session; clients typically add it to the model's context.</summary>
    public const string Instructions =
        """
        RoslynReview gives read-only, compiler-accurate navigation of the C# solution under review.
        To review a change:
        1. Call get_changed_symbols with baseRef (the branch the change targets, e.g. "main") or with a unified diff
           to learn which methods, properties and types the change touches.
        2. Call get_symbol_source for the symbols you need to read. Long types come back as an outline with member ids.
        3. Call find_callers on changed members to see who depends on them before judging the impact.
        Symbol ids are documentation comment ids such as
        "M:MyApp.Orders.OrderService.PlaceOrderAsync(MyApp.Orders.Order,System.Threading.CancellationToken)".
        Pass ids exactly as a previous tool returned them.
        """;

    /// <summary>
    /// Reads <c>--solution</c> and <c>--repo-root</c> (or the <c>ROSLYN_REVIEW_SOLUTION</c> and
    /// <c>ROSLYN_REVIEW_REPO_ROOT</c> environment variables). Without a solution argument, looks for a
    /// single .slnx, .sln or .csproj in <paramref name="currentDirectory"/>.
    /// </summary>
    public static WorkspaceOptions Resolve(IConfiguration configuration, string currentDirectory)
    {
        var solution = configuration["solution"] ?? configuration["ROSLYN_REVIEW_SOLUTION"] ?? Discover(currentDirectory);
        solution = Path.GetFullPath(solution, currentDirectory);
        if (!File.Exists(solution))
        {
            throw new ServerConfigurationException($"Solution file not found: {solution}");
        }

        var repositoryRoot = configuration["repo-root"] ?? configuration["ROSLYN_REVIEW_REPO_ROOT"];
        if (repositoryRoot is not null)
        {
            repositoryRoot = Path.GetFullPath(repositoryRoot, currentDirectory);
            if (!Directory.Exists(repositoryRoot))
            {
                throw new ServerConfigurationException($"Repository root not found: {repositoryRoot}");
            }
        }

        return new WorkspaceOptions(solution, repositoryRoot);
    }

    private static string Discover(string directory)
    {
        foreach (var extension in (string[])[".slnx", ".sln", ".csproj"])
        {
            var candidates = Directory.EnumerateFiles(directory)
                .Where(file => string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count > 1)
            {
                throw new ServerConfigurationException(
                    $"Found several {extension} files in {directory}; choose one with --solution <path>.");
            }
        }

        throw new ServerConfigurationException(
            $"No .slnx, .sln or .csproj found in {directory}. Pass --solution <path> or set ROSLYN_REVIEW_SOLUTION.");
    }
}
