using Crm.Core;
using Crm.Core.Data;
using Crm.Core.Services;
using Crm.Mcp;
using Crm.Mcp.Stdio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// stdio-сервер запускает сам MCP-клиент (Claude Desktop, Claude Code, Cursor) как дочерний процесс.
// stdout занят протоколом, поэтому любые логи — только в stderr.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Клиент запускает процесс из своей рабочей папки, поэтому appsettings.json ищем рядом с exe
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddCrmCore(builder.Configuration);
builder.Services.AddSingleton<StdioCurrentUser>();
builder.Services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<StdioCurrentUser>());

builder.Services
    .AddCrmMcpServer(auditChannel: "mcp-stdio")
    .WithStdioServerTransport();

var host = builder.Build();

await host.Services.InitializeCrmDatabaseAsync();
await host.Services.GetRequiredService<StdioCurrentUser>().SignInAsync(builder.Configuration["Crm:StdioUserEmail"]);

await host.RunAsync();
