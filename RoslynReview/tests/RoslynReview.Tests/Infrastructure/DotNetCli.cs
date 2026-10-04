using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RoslynReview.Tests.Infrastructure;

internal static class DotNetCli
{
    /// <summary>The dotnet host that runs the tests, so restores and the server use the same installation.</summary>
    public static string Executable { get; } = FindExecutable();

    /// <summary>MSBuildWorkspace needs restored projects (project.assets.json) to resolve references.</summary>
    public static async Task RestoreAsync(string projectOrSolution, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["restore", projectOrSolution, "--nologo", "--verbosity", "quiet", "--disable-build-servers"])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet restore {projectOrSolution} failed:\n{await output}\n{await error}");
        }
    }

    private static string FindExecutable()
    {
        // <dotnet root>/shared/Microsoft.NETCore.App/<version>/ -> <dotnet root>/dotnet
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var host = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(host) ? host : "dotnet";
    }
}
