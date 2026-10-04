using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests.EndToEnd;

/// <summary>Starts the real server over stdio, the way Claude Desktop or VS Code would.</summary>
public sealed class McpServerFixture : IAsyncLifetime
{
    private readonly List<string> _serverLog = [];

    public McpClient Client { get; private set; } = null!;

    public IReadOnlyList<string> ServerLog
    {
        get
        {
            lock (_serverLog)
            {
                return [.. _serverLog];
            }
        }
    }

    public async ValueTask InitializeAsync()
    {
        await DotNetCli.RestoreAsync(TestPaths.SampleShopSolution);
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "roslyn-review",
            Command = DotNetCli.Executable,
            Arguments =
            [
                Path.Combine(AppContext.BaseDirectory, "RoslynReview.McpServer.dll"),
                "--solution", TestPaths.SampleShopSolution,
                "--repo-root", TestPaths.SampleShop,
            ],
            StandardErrorLines = line =>
            {
                lock (_serverLog)
                {
                    _serverLog.Add(line);
                }
            },
        });
        Client = await McpClient.CreateAsync(transport);
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }
    }
}

[Collection(SampleShopCollection.Name)]
public sealed class McpServerTests(McpServerFixture server) : IClassFixture<McpServerFixture>
{
    private const string PlaceOrderId =
        "M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)";

    [Fact]
    public async Task Exposes_three_read_only_tools_and_usage_instructions()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "find_callers", "get_changed_symbols", "get_symbol_source" },
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint == true, tool.Name));
        Assert.Contains("get_changed_symbols", server.Client.ServerInstructions);
    }

    [Fact]
    public async Task Get_changed_symbols_returns_structured_json()
    {
        var result = await CallAsync("get_changed_symbols", new() { ["diff"] = TestPaths.ReadDiff("02-removals.diff") });

        Assert.False(result.IsError == true);
        Assert.True(result.StructuredContent.HasValue);
        var symbols = result.StructuredContent.Value.GetProperty("symbols").EnumerateArray().ToList();
        Assert.Equal(3, symbols.Count);
        Assert.Contains(symbols, symbol =>
            symbol.GetProperty("id").GetString() == PlaceOrderId &&
            symbol.GetProperty("change").GetString() == "modified" &&
            symbol.GetProperty("changedLines").GetInt32() == 1);
    }

    [Fact]
    public async Task Get_changed_symbols_needs_a_base_ref_or_a_diff()
    {
        var result = await CallAsync("get_changed_symbols", []);

        Assert.True(result.IsError == true);
        Assert.Contains("Pass exactly one of baseRef", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task Get_symbol_source_returns_numbered_source()
    {
        var result = await CallAsync("get_symbol_source", new() { ["symbolId"] = PlaceOrderId, ["maxLines"] = 40 });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("14 |     public async Task<OrderResult> PlaceOrderAsync(Order order, CancellationToken cancellationToken)", text);
    }

    [Fact]
    public async Task Json_results_keep_generic_signatures_readable()
    {
        var result = await CallAsync("find_callers", new() { ["symbolId"] = "M:SampleShop.Orders.OrderService.CalculateDiscountRate(SampleShop.Orders.Order)" });

        var json = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("OrderEndpoints.PreviewDiscount(IReadOnlyList<OrderLine>)", json);
    }

    [Fact]
    public async Task An_unknown_symbol_is_a_tool_error_the_model_can_act_on()
    {
        var result = await CallAsync("find_callers", new() { ["symbolId"] = "M:SampleShop.Orders.OrderService.Nope" });

        Assert.True(result.IsError == true);
        Assert.Contains("No symbol with id", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task Logs_go_to_stderr_and_never_corrupt_the_protocol()
    {
        await CallAsync("find_callers", new() { ["symbolId"] = PlaceOrderId });

        // The console logger writes on a background thread, so give the last lines a moment to arrive.
        for (var attempt = 0; attempt < 50 && !server.ServerLog.Any(IsLoadedMessage); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Contains(server.ServerLog, IsLoadedMessage);
    }

    private static bool IsLoadedMessage(string line) => line.Contains("Loaded 2 projects", StringComparison.Ordinal);

    private async Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        await server.Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
}
