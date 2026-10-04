using System.ComponentModel;
using System.Diagnostics;

namespace RoslynReview.Core.Diff;

/// <summary>git could not produce the diff. The message is meant for the agent.</summary>
public sealed class GitDiffException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Produces the diff under review straight from the repository, so the agent does not have to pass it around.</summary>
public static class GitDiff
{
    /// <summary>
    /// Runs <c>git diff --merge-base &lt;baseRef&gt;</c>: the working tree compared with the point where the current
    /// branch left <paramref name="baseRef"/>. That covers the branch's commits plus uncommitted edits to tracked files,
    /// which is exactly the source the workspace reads. Untracked files are not included.
    /// </summary>
    public static async Task<string> AgainstAsync(string repositoryRoot, string baseRef, CancellationToken cancellationToken = default)
    {
        // The ref comes from the model: never let it be parsed as an option.
        if (string.IsNullOrWhiteSpace(baseRef) || baseRef.StartsWith('-') || baseRef.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new GitDiffException($"'{baseRef}' is not a git ref. Pass a branch, tag or commit such as 'main' or 'origin/main'.");
        }

        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0"; // do not refresh .git/index as a side effect
        foreach (var argument in (string[])["diff", "--no-color", "--no-ext-diff", "--no-textconv", "--merge-base", "--end-of-options", baseRef])
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new GitDiffException("Could not start git.");
        }
        catch (Win32Exception ex)
        {
            throw new GitDiffException("git is not installed or not on PATH; pass the diff text instead.", ex);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            if (process.ExitCode != 0)
                throw new GitDiffException($"git diff --merge-base {baseRef} failed: {(await error.ConfigureAwait(false)).Trim()}");

            return await output.ConfigureAwait(false);
        }
    }
}
