using System.Diagnostics;
using RoslynReview.Core.Diff;

namespace RoslynReview.Tests.Diff;

public sealed class GitDiffTests
{
    [Fact]
    public async Task Covers_branch_commits_and_uncommitted_edits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = Directory.CreateTempSubdirectory("roslynreview-git-").FullName;
        try
        {
            var file = Path.Combine(repository, "A.cs");
            await GitAsync(repository, cancellationToken, "init", "--quiet", "--initial-branch=main");
            await File.WriteAllTextAsync(file, "class A\n{\n}\n", cancellationToken);
            await GitAsync(repository, cancellationToken, "add", "A.cs");
            await GitAsync(repository, cancellationToken, "commit", "--quiet", "--no-verify", "-m", "base");
            await GitAsync(repository, cancellationToken, "checkout", "--quiet", "-b", "feature");
            await File.WriteAllTextAsync(file, "class A\n{\n    int committed;\n}\n", cancellationToken);
            await GitAsync(repository, cancellationToken, "commit", "--quiet", "--no-verify", "-am", "feature work");
            await File.WriteAllTextAsync(file, "class A\n{\n    int committed;\n    int uncommitted;\n}\n", cancellationToken);

            var diff = await GitDiff.AgainstAsync(repository, "main", cancellationToken);

            var changed = Assert.Single(UnifiedDiffParser.Parse(diff));
            Assert.Equal("A.cs", changed.Path);
            Assert.Equal(new[] { new DiffLine(3, "    int committed;"), new DiffLine(4, "    int uncommitted;") }, changed.AddedLines);
        }
        finally
        {
            try
            {
                Directory.Delete(repository, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // git marks object files read-only on Windows; a leftover temp folder is harmless.
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("--output=/tmp/leak")]
    [InlineData("main; rm -rf /")]
    public async Task Refs_that_could_be_parsed_as_options_are_rejected(string baseRef)
    {
        await Assert.ThrowsAsync<GitDiffException>(() =>
            GitDiff.AgainstAsync(Path.GetTempPath(), baseRef, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unknown_ref_is_reported_with_gits_message()
    {
        var error = await Assert.ThrowsAsync<GitDiffException>(() =>
            GitDiff.AgainstAsync(Path.GetTempPath(), "no-such-branch", TestContext.Current.CancellationToken));

        Assert.Contains("no-such-branch", error.Message);
    }

    // Independent of the user's git config: no identity, signing or commit hooks needed.
    private static async Task GitAsync(string repository, CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", .. arguments])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {await error}");
    }
}
