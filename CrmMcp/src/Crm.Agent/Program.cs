using System.Globalization;
using System.Text;
using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Crm.Agent;

/// <summary>
/// Консольный агент: Claude через Claude API + инструменты CRM через MCP.
/// Модель решает, какой инструмент вызвать; MCP-клиент выполняет вызов на сервере CRM;
/// изменяющие вызовы сначала подтверждает человек.
/// </summary>
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        // 1. Ключ Claude API: из user-secrets/appsettings, иначе SDK сам возьмёт ANTHROPIC_API_KEY
        var clientOptions = new ClientOptions();

        if (config["Anthropic:ApiKey"] is { Length: > 0 } apiKey)
            clientOptions.ApiKey = apiKey;

        if (string.IsNullOrWhiteSpace(clientOptions.ApiKey) && string.IsNullOrWhiteSpace(clientOptions.AuthToken))
        {
            AgentConsole.Error("""
                Не задан ключ Claude API. Сохрани его в user-secrets:
                  dotnet user-secrets set "Anthropic:ApiKey" "<ключ>" --project src/Crm.Agent
                или задай переменную окружения ANTHROPIC_API_KEY.
                """);
            return 1;
        }

        // 2. Подключаемся к MCP-серверу CRM и получаем его инструменты
        var useStdio = args.Contains("--stdio")
            || string.Equals(config["Crm:Transport"], "stdio", StringComparison.OrdinalIgnoreCase);

        McpClient mcpClient;

        try
        {
            mcpClient = await McpClient.CreateAsync(
                CreateTransport(config, useStdio),
                new McpClientOptions { ClientInfo = new Implementation { Name = "crm-agent", Version = "1.0.0" } });
        }
        catch (Exception ex)
        {
            AgentConsole.Error(useStdio
                ? $"Не удалось запустить stdio-сервер CRM: {ex.Message}\nПроверь путь Crm:StdioServerDll и что PostgreSQL запущен."
                : $"Не удалось подключиться к {config["Crm:McpUrl"]}: {ex.Message}\nЗапусти Crm.Api (dotnet run --project src/Crm.Api) или агента с флагом --stdio.");
            return 1;
        }

        await using var _ = mcpClient;

        IList<McpClientTool> tools = await mcpClient.ListToolsAsync();

        // 3. Claude как IChatClient. FunctionInvokingChatClient сам гоняет цикл «модель → инструмент → модель»,
        //    а ToolApproval перехватывает каждый вызов: печатает его и спрашивает подтверждение для изменений.
        var model = config["Anthropic:Model"] ?? "claude-sonnet-5-5";
        var maxOutputTokens = int.TryParse(config["Anthropic:MaxOutputTokens"], CultureInfo.InvariantCulture, out var max) ? max : 4096;

        using var anthropic = new AnthropicClient(clientOptions);

        using var chatClient = anthropic
            .AsIChatClient(model, maxOutputTokens)
            .AsBuilder()
            .UseFunctionInvocation(configure: invoker =>
            {
                invoker.MaximumIterationsPerRequest = 15;
                invoker.FunctionInvoker = ToolApproval.InvokeAsync;
            })
            .Build();

        var options = new ChatOptions
        {
            // Инструкции сервера CRM (правила работы с инструментами) — часть системного промпта
            Instructions = BuildInstructions(mcpClient.ServerInstructions),
            Tools = [.. tools],
        };

        List<ChatMessage> history = [];

        AgentConsole.Banner(mcpClient.ServerInfo.Name, tools.Count, model, useStdio ? "stdio" : config["Crm:McpUrl"]!);

        while (AgentConsole.ReadUserInput() is { } input)
        {
            if (input is "exit" or "выход")
                break;

            switch (input)
            {
                case "":
                    continue;
                case "/reset":
                    history.Clear();
                    AgentConsole.Info("История диалога очищена.");
                    continue;
                case "/tools":
                    AgentConsole.Tools(tools);
                    continue;
                case "/prompts":
                    AgentConsole.Prompts(await mcpClient.ListPromptsAsync());
                    continue;
            }

            var turnStart = history.Count;

            try
            {
                if (input.StartsWith("/prompt ", StringComparison.Ordinal))
                {
                    // Промпт сервера: например «/prompt call_prep contactId=1» — сервер сам подставит данные из CRM
                    history.AddRange(await GetPromptMessagesAsync(mcpClient, input));
                }
                else
                {
                    history.Add(new ChatMessage(ChatRole.User, input));
                }

                var updates = new List<ChatResponseUpdate>();

                AgentConsole.BeginAnswer();

                await foreach (var update in chatClient.GetStreamingResponseAsync(history, options))
                {
                    updates.Add(update);
                    AgentConsole.Write(update.Text);
                }

                // В историю попадают и ответы модели, и вызовы инструментов с результатами —
                // так следующий вопрос учитывает всё, что уже было сделано
                history.AddMessages(updates);
                AgentConsole.EndAnswer(updates);
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
        }

        return 0;
    }

    private static IClientTransport CreateTransport(IConfiguration config, bool useStdio)
    {
        IClientTransport res;

        if (useStdio)
        {
            // Агент сам запускает stdio-сервер как дочерний процесс — как это делает Claude Desktop
            var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, config["Crm:StdioServerDll"]!));

            res = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "crm",
                Command = "dotnet",
                Arguments = [dll],
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["Crm__StdioUserEmail"] = config["Crm:StdioUserEmail"],
                },
            });

            return res;
        }

        res = new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "crm",
            Endpoint = new Uri(config["Crm:McpUrl"] ?? "http://localhost:5080/mcp"),
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {config["Crm:ApiKey"]}",
            },
        });

        return res;
    }

    private static async Task<IList<ChatMessage>> GetPromptMessagesAsync(McpClient mcp, string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            throw new ArgumentException("Формат: /prompt <имя> [аргумент=значение ...]");

        var arguments = parts.Skip(2)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => (object?)pair[1]);

        var prompt = await mcp.GetPromptAsync(parts[1], arguments);
        return prompt.ToChatMessages();
    }

    private static string BuildInstructions(string? serverInstructions) => $"""
        Ты — ассистент отдела продаж и работаешь с CRM только через доступные инструменты.
        Сегодня {DateTime.Now:yyyy-MM-dd} ({DateTime.Now.DayOfWeek}).
        Отвечай по-русски, коротко и по делу. Если инструмент вернул ошибку, объясни её простыми словами.
        Изменяющие вызовы пользователь подтверждает отдельно; если он отказался, не повторяй вызов без новой просьбы.

        Правила сервера CRM:
        {serverInstructions}
        """;
}
