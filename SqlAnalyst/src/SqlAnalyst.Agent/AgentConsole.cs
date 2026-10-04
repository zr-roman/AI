using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace SqlAnalyst.Agent;

/// <summary>Весь вывод в консоль: диалог, трассировка вызовов инструментов, срабатывания защиты.</summary>
internal static class AgentConsole
{
    private static readonly JsonSerializerOptions ArgumentsJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Banner(string model, int toolCount, IReadOnlyList<string> rejected)
    {
        WriteLine($"SQL-аналитик: модель {model}, инструментов: {toolCount}.", ConsoleColor.Green);

        if (rejected.Count > 0)
        {
            WriteLine($"Не подключены (не read-only): {string.Join(", ", rejected)}", ConsoleColor.DarkYellow);
        }

        WriteLine("Спроси, например: «какие клиенты ушли в прошлом квартале?» или «нарисуй MRR по месяцам за год».", ConsoleColor.DarkGray);
        WriteLine("Команды: /reset — забыть диалог, exit — выход.", ConsoleColor.DarkGray);
    }

    public static string? ReadUserInput()
    {
        Write("\nВы> ", ConsoleColor.Cyan);
        return Console.ReadLine()?.Trim();
    }

    public static void Answer(string text)
    {
        Write("\nClaude> ", ConsoleColor.Yellow);
        Console.WriteLine(text);
    }

    public static void Usage(UsageDetails? usage)
    {
        if (usage is { InputTokenCount: > 0 } or { OutputTokenCount: > 0 })
        {
            WriteLine($"  [токены: вход {usage.InputTokenCount}, выход {usage.OutputTokenCount}]", ConsoleColor.DarkGray);
        }
    }

    public static void GuardSummary(int injectionWarnings, int blockedQueries)
    {
        if (injectionWarnings > 0)
        {
            WriteLine($"  🛡 В данных обнаружены и скрыты попытки prompt injection (результатов с предупреждением: {injectionWarnings}).", ConsoleColor.Magenta);
        }

        if (blockedQueries > 0)
        {
            WriteLine($"  🛡 Отклонено запросов: {blockedQueries}.", ConsoleColor.Magenta);
        }
    }

    public static void ToolCall(string name, AIFunctionArguments arguments)
    {
        var args = string.Join(", ", arguments.Select(a => $"{a.Key}: {JsonSerializer.Serialize(a.Value, ArgumentsJson)}"));
        WriteLine($"\n  → {name}({args})", ConsoleColor.DarkGray);
    }

    public static void ToolDenied(string reason) => WriteLine($"    ✗ заблокировано: {reason}", ConsoleColor.DarkRed);

    public static void ToolResult(string text, bool isError)
    {
        var firstLine = text.Split('\n', 2)[0].Trim();

        if (firstLine.Length > 110)
        {
            firstLine = firstLine[..110] + "…";
        }

        WriteLine($"    {(isError ? "✗" : "✓")} {firstLine}", isError ? ConsoleColor.DarkRed : ConsoleColor.DarkGray);
    }

    public static void Info(string message) => WriteLine(message, ConsoleColor.DarkGray);

    public static void Warning(string message) => WriteLine(message, ConsoleColor.Magenta);

    public static void Error(string message) => WriteLine(message, ConsoleColor.Red);

    /// <summary>Результат MCP-инструмента приходит как TextContent или JSON CallToolResult (isError = true при ошибке).</summary>
    public static (string Text, bool IsError) Describe(object? result) => result switch
    {
        null => ("(пустой ответ)", false),
        string s => (s, false),
        TextContent text => (text.Text, false),
        CallToolResult call => (string.Join("\n", call.Content.OfType<TextContentBlock>().Select(c => c.Text)), call.IsError == true),
        IEnumerable<AIContent> contents => (string.Join(" ", contents.OfType<TextContent>().Select(c => c.Text)), false),
        JsonElement { ValueKind: JsonValueKind.Object } json => (
            json.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0
                && content[0].TryGetProperty("text", out var t) ? t.GetString() ?? "" : json.ToString(),
            json.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True),
        _ => (result.ToString() ?? "", false),
    };

    private static void Write(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    private static void WriteLine(string text, ConsoleColor color) => Write(text + Environment.NewLine, color);
}
