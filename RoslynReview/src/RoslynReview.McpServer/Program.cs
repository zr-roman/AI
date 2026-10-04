using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoslynReview.Core.Workspace;
using RoslynReview.McpServer;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

WorkspaceOptions workspace;
try
{
    workspace = ServerConfiguration.Resolve(builder.Configuration, Directory.GetCurrentDirectory());
}
catch (ServerConfigurationException ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 2;
}

builder.Services.AddSingleton(workspace);
builder.Services.AddSingleton<SolutionHost>();
builder.Services.AddHostedService<SolutionWarmup>();

builder.Services
    .AddMcpServer(options => options.ServerInstructions = ServerConfiguration.Instructions)
    .WithStdioServerTransport()
    .WithTools<ReviewTools>(ReviewTools.SerializerOptions);

await builder.Build().RunAsync();
return 0;
