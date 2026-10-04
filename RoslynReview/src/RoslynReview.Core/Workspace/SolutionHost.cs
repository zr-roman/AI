using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynReview.Core.Workspace;

/// <summary>
/// Opens the solution once with <see cref="MSBuildWorkspace"/> and hands out cached snapshots.
/// </summary>
/// <remarks>
/// Loading runs a design-time build, so the solution must be restored (<c>dotnet restore</c>) first,
/// and the repository's MSBuild logic is executed: only point the server at code you trust,
/// or run it in a container. Edits to existing files are picked up on the next request;
/// added or removed files and projects need a server restart.
/// </remarks>
public sealed class SolutionHost : IDisposable
{
    private readonly WorkspaceOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private DateTime _snapshotTimeUtc;

    public SolutionHost(WorkspaceOptions options, ILogger<SolutionHost>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<SolutionHost>.Instance;
        Paths = RepositoryPaths.Create(options.SolutionPath, options.RepositoryRoot);
    }

    public RepositoryPaths Paths { get; }

    /// <summary>Returns the current snapshot, loading the solution on first use.</summary>
    /// <exception cref="SolutionLoadException">The solution file is missing or MSBuild failed to open it.</exception>
    public async Task<SolutionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _solution = _solution is null
                ? await LoadAsync(cancellationToken).ConfigureAwait(false)
                : RefreshChangedDocuments(_solution, cancellationToken);
            return new SolutionSnapshot(_solution, Paths);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _gate.Dispose();
    }

    private async Task<Solution> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(_options.SolutionPath);
        if (!File.Exists(path))
        {
            throw new SolutionLoadException($"Solution or project file not found: {path}");
        }

        var loadStartedUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Loading {Path}", path);

        _workspace?.Dispose();
        _workspace = MSBuildWorkspace.Create();

        Solution solution;
        try
        {
            solution = path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? (await _workspace.OpenProjectAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false)).Solution
                : await _workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SolutionLoadException($"MSBuild could not open {path}: {ex.Message}", ex);
        }

        foreach (var diagnostic in _workspace.Diagnostics)
        {
            var level = diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? LogLevel.Warning : LogLevel.Debug;
            _logger.Log(level, "MSBuild: {Message}", diagnostic.Message);
        }

        await WarnAboutUnrestoredProjectsAsync(solution, cancellationToken).ConfigureAwait(false);

        _snapshotTimeUtc = loadStartedUtc;
        _logger.LogInformation(
            "Loaded {ProjectCount} projects with {DocumentCount} documents in {Seconds:F1} s",
            solution.ProjectIds.Count,
            solution.Projects.Sum(project => project.DocumentIds.Count),
            stopwatch.Elapsed.TotalSeconds);
        return solution;
    }

    private async Task WarnAboutUnrestoredProjectsAsync(Solution solution, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation?.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error)
            {
                _logger.LogWarning(
                    "Project {Project} has no framework references, so symbols will not resolve. Run 'dotnet restore' and restart the server.",
                    project.Name);
            }
        }
    }

    // Cheap staleness check: re-read only the files whose timestamp moved since the last snapshot.
    private Solution RefreshChangedDocuments(Solution solution, CancellationToken cancellationToken)
    {
        var checkStartedUtc = DateTime.UtcNow;
        var refreshed = solution;
        var count = 0;

        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.FilePath is not { } path)
            {
                continue;
            }

            try
            {
                if (File.GetLastWriteTimeUtc(path) <= _snapshotTimeUtc)
                {
                    continue;
                }

                using var stream = File.OpenRead(path);
                refreshed = refreshed.WithDocumentText(document.Id, SourceText.From(stream), PreservationMode.PreserveIdentity);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not re-read {Path}", path);
            }
        }

        _snapshotTimeUtc = checkStartedUtc;
        if (count > 0)
        {
            _logger.LogInformation("Refreshed {Count} changed documents", count);
        }

        return refreshed;
    }
}
