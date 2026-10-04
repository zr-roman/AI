using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SqlAnalyst.Tests.Integration;

/// <summary>Настоящий сервер по stdio — так его запускают агент, Claude Desktop или Claude Code.</summary>
public sealed class McpServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exposes_only_read_only_tools_and_runs_queries()
    {
        await using var client = await StartAsync(DatabaseFixture.AnalystConnection, "Нет БД.");

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(["describe_table", "list_tables", "run_query", "sample_rows"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations is { ReadOnlyHint: true, OpenWorldHint: false }, t.Name));
        Assert.Contains("untrusted-data", client.ServerInstructions);

        var list = await client.CallToolAsync("list_tables", cancellationToken: Ct);
        Assert.Contains("customer_churn", Text(list));

        var ok = await client.CallToolAsync("run_query", new Dictionary<string, object?>
        {
            ["sql"] = "SELECT segment, count(*) AS customers FROM customers GROUP BY segment ORDER BY 2 DESC",
            ["purpose"] = "test",
        }, cancellationToken: Ct);
        Assert.NotEqual(true, ok.IsError);
        Assert.Contains("SMB", Text(ok));

        var denied = await client.CallToolAsync("run_query", new Dictionary<string, object?>
        {
            ["sql"] = "DELETE FROM customers",
            ["purpose"] = "test",
        }, cancellationToken: Ct);
        Assert.True(denied.IsError);

        var unknown = await client.CallToolAsync("describe_table", new Dictionary<string, object?> { ["table"] = "api_keys" }, cancellationToken: Ct);
        Assert.True(unknown.IsError);
    }

    [Fact]
    public async Task Refuses_to_start_with_a_privileged_connection()
    {
        Assert.SkipWhen(DatabaseFixture.AdminConnection is null, $"Не задана {DatabaseFixture.AdminVariable}.");

        // Сервер завершается до рукопожатия MCP — клиент не может подключиться
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await StartAsync(DatabaseFixture.AdminConnection, "");
        });
    }

    private static async Task<McpClient> StartAsync(string? connectionString, string skipReason)
    {
        Assert.SkipWhen(connectionString is null, skipReason);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "sql-analyst",
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "SqlAnalyst.McpServer.dll")],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["Analyst__ConnectionString"] = connectionString,
                ["Analyst__AuditLogPath"] = "",
            },
        });

        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
