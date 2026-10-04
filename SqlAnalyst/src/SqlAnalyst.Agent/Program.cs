using System.Globalization;
using System.Text;
using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SqlAnalyst.Agent;

/// <summary>
/// Консольный SQL-аналитик: Claude через Claude API, данные — через read-only MCP-сервер, графики — локально.
/// Режимы: интерактивный чат или один вопрос: dotnet run -- --ask "какие клиенты ушли в прошлом квартале?"
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var clientOptions = new ClientOptions();

        if (config["Anthropic:ApiKey"] is { Length: > 0 } apiKey)
        {
            clientOptions.ApiKey = apiKey;
        }

        if (string.IsNullOrWhiteSpace(clientOptions.ApiKey) && string.IsNullOrWhiteSpace(clientOptions.AuthToken))
        {
            AgentConsole.Error("""
                Не задан ключ Claude API. Сохрани его в user-secrets:
                  dotnet user-secrets set "Anthropic:ApiKey" "<ключ>" --project src/SqlAnalyst.Agent
                или задай переменную окружения ANTHROPIC_API_KEY.
                """);
            return 1;
        }

        McpClient mcp;
        try
        {
            mcp = await McpClient.CreateAsync(
                CreateTransport(config),
                new McpClientOptions { ClientInfo = new Implementation { Name = "sql-analyst-agent", Version = "0.1.0" } });
        }
        catch (Exception ex)
        {
            AgentConsole.Error($"""
                Не удалось запустить MCP-сервер: {ex.Message}
                Собери решение (dotnet build), запусти PostgreSQL (docker compose up -d) и проверь путь Agent:ServerDll.
                Если сервер отказался стартовать из-за прав роли — смотри его сообщение выше.
                """);
            return 1;
        }

        await using var _ = mcp;

        var (tools, rejected) = ToolGuard.FilterTools(await mcp.ListToolsAsync());
        var chartTool = new ChartTool(Path.GetFullPath(config["Agent:ChartsDirectory"] ?? "charts"));
        tools.Add(AIFunctionFactory.Create(chartTool.RenderChart, ChartTool.Name));

        var guard = new ToolGuard(int.TryParse(config["Agent:MaxToolCallsPerQuestion"], CultureInfo.InvariantCulture, out var budget) ? budget : 12);
        var model = config["Anthropic:Model"] ?? "claude-sonnet-5-5";
        var maxOutputTokens = int.TryParse(config["Anthropic:MaxOutputTokens"], CultureInfo.InvariantCulture, out var max) ? max : 4096;

        using var anthropic = new AnthropicClient(clientOptions);
        using var chat = anthropic
            .AsIChatClient(model, maxOutputTokens)
            .AsBuilder()
            .UseFunctionInvocation(configure: invoker =>
            {
                invoker.MaximumIterationsPerRequest = 15;
                invoker.FunctionInvoker = guard.InvokeAsync;
            })
            .Build();

        var options = new ChatOptions
        {
            Instructions = SystemPrompt.Build(mcp.ServerInstructions, DateTime.Now),
            Tools = tools,
        };

        var canaries = config.GetSection("Agent:CanaryTokens").GetChildren().Select(c => c.Value!).Where(v => v.Length > 0).ToList();
        var oneShot = GetAskArgument(args);
        List<ChatMessage> history = [];

        if (oneShot is null)
        {
            AgentConsole.Banner(model, tools.Count, rejected);
        }

        while ((oneShot ?? AgentConsole.ReadUserInput()) is { } input)
        {
            if (input is "exit" or "выход")
            {
                break;
            }

            if (input == "/reset")
            {
                history.Clear();
                AgentConsole.Info("История диалога очищена.");
                continue;
            }

            if (input.Length == 0)
            {
                continue;
            }

            var turnStart = history.Count;
            guard.StartQuestion();

            try
            {
                history.Add(new ChatMessage(ChatRole.User, input));
                var response = await chat.GetResponseAsync(history, options);
                history.AddMessages(response);

                AgentConsole.Answer(OutputGuard.Check(response.Text, mcp.ServerInstructions, canaries));
                AgentConsole.GuardSummary(guard.InjectionWarnings, guard.BlockedQueries);
                AgentConsole.Usage(response.Usage);
            }
            catch (AnthropicUnauthorizedException)
            {
                history.RemoveRange(turnStart, history.Count - turnStart);
                AgentConsole.Error("Claude API отклонил ключ (401). Проверь Anthropic:ApiKey или ANTHROPIC_API_KEY.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                history.RemoveRange(turnStart, history.Count - turnStart);
                AgentConsole.Error($"{ex.GetType().Name}: {ex.Message}");
            }

            if (oneShot is not null)
            {
                break;
            }
        }

        return 0;
    }

    private static string? GetAskArgument(string[] args)
    {
        var index = Array.IndexOf(args, "--ask");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static StdioClientTransport CreateTransport(IConfiguration config)
    {
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, config["Agent:ServerDll"]!));
        var environment = new Dictionary<string, string?>();

        if (config["Agent:ConnectionString"] is { Length: > 0 } connectionString)
        {
            environment["Analyst__ConnectionString"] = connectionString;
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "sql-analyst",
            Command = "dotnet",
            Arguments = [dll],
            EnvironmentVariables = environment,
            StandardErrorLines = line =>
            {
                // Сервер пишет в stderr аудит и ошибки старта; на консоль выводим только важное
                if (!line.TrimStart().StartsWith("info:", StringComparison.Ordinal) && !line.Contains("audit {", StringComparison.Ordinal))
                {
                    AgentConsole.Info("  [сервер] " + line);
                }
            },
        });
    }
}
