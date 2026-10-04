using RoslynReview.Core.Workspace;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests.Workspace;

public sealed class SolutionHostTests
{
    [Fact]
    public async Task Edits_to_existing_files_are_picked_up_without_reloading()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var copy = CopySampleShop();
        try
        {
            var solutionPath = Path.Combine(copy, "SampleShop.slnx");
            await DotNetCli.RestoreAsync(solutionPath, cancellationToken);
            using var host = new SolutionHost(new WorkspaceOptions(solutionPath));
            await host.GetSnapshotAsync(cancellationToken);

            var file = Path.Combine(copy, "src", "SampleShop.Core", "Pricing", "PriceCalculator.cs");
            var source = await File.ReadAllTextAsync(file, cancellationToken);
            await File.WriteAllTextAsync(file, source.Replace("0.05m", "0.07m", StringComparison.Ordinal), cancellationToken);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

            var snapshot = await host.GetSnapshotAsync(cancellationToken);

            var document = snapshot.Solution.GetDocument(Assert.Single(snapshot.FindDocuments("src/SampleShop.Core/Pricing/PriceCalculator.cs")));
            Assert.NotNull(document);
            Assert.Contains("0.07m", (await document.GetTextAsync(cancellationToken)).ToString());
        }
        finally
        {
            TryDelete(copy);
        }
    }

    [Fact]
    public async Task A_missing_solution_is_a_load_error()
    {
        using var host = new SolutionHost(new WorkspaceOptions(Path.Combine(TestPaths.Fixtures, "Missing.slnx")));

        var error = await Assert.ThrowsAsync<SolutionLoadException>(() => host.GetSnapshotAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Missing.slnx", error.Message);
    }

    [Fact]
    public void Paths_are_relative_to_the_nearest_git_root()
    {
        var root = Directory.CreateTempSubdirectory("roslynreview-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var project = Path.Combine(root, "src", "App", "App.csproj");

            var paths = RepositoryPaths.Create(project);

            Assert.Equal(root, paths.Root);
            Assert.Equal("src/App/Program.cs", paths.ToRelative(Path.Combine(root, "src", "App", "Program.cs")));
            Assert.Equal(Path.Combine(root, "src", "App", "Program.cs"), paths.ToAbsolute("src/App/Program.cs"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CopySampleShop()
    {
        var target = Directory.CreateTempSubdirectory("roslynreview-").FullName;
        foreach (var source in Directory.EnumerateFiles(TestPaths.SampleShop, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(TestPaths.SampleShop, source);
            if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }

        return target;
    }

    // MSBuild's build host may still hold files for a moment on Windows; a leftover temp folder is harmless.
    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
