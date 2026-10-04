using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoslynReview.Core.Workspace;

namespace RoslynReview.McpServer;

/// <summary>Starts loading the solution at startup so the first tool call does not wait for MSBuild.</summary>
internal sealed class SolutionWarmup(SolutionHost host, ILogger<SolutionWarmup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            await host.GetSnapshotAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Not fatal: the next tool call retries the load and reports the error to the client.
            logger.LogError(ex, "Loading the solution failed");
        }
    }
}
