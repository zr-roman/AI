using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlAnalyst.Core;
using SqlAnalyst.Core.Execution;
using SqlAnalyst.McpServer;

// stdio-сервер запускает MCP-клиент (агент, Claude Desktop, Claude Code) как дочерний процесс.
// stdout занят протоколом, поэтому все логи — в stderr.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

try
{
    builder.Services.AddSqlAnalyst(builder.Configuration);
}
catch (InvalidOperationException ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 2;
}

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "sql-analyst", Title = "SQL-аналитик (read-only)", Version = "0.1.0" };
        options.ServerInstructions = ServerInstructions.Text;
    })
    .WithStdioServerTransport()
    .WithTools<AnalystTools>();

var host = builder.Build();

try
{
    await host.Services.InitializeAnalystAsync();
}
catch (UnsafeConnectionException ex)
{
    // Fail closed: с небезопасной ролью сервер не работает вовсе
    await Console.Error.WriteLineAsync(ex.Message);
    return 3;
}
catch (Npgsql.NpgsqlException ex)
{
    await Console.Error.WriteLineAsync($"Не удалось подключиться к PostgreSQL: {ex.Message}");
    return 4;
}

await host.RunAsync();
return 0;
