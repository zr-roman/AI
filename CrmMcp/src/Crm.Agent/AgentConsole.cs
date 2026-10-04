using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Crm.Agent;

/// <summary>Весь вывод в консоль: диалог, трассировка вызовов инструментов, подтверждения.</summary>
internal static class AgentConsole
{
    private static readonly JsonSerializerOptions ArgumentsJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Banner(string server, int toolCount, string model, string endpoint)
    {
        WriteLine($"CRM-агент: модель {model}, MCP-сервер «{server}» ({endpoint}), инструментов: {toolCount}.", ConsoleColor.Green);
        WriteLine("Команды: /tools, /prompts, /prompt <имя> [арг=значение], /reset, exit", ConsoleColor.DarkGray);
    }

    public static string? ReadUserInput()
    {
        Write("\nВы> ", ConsoleColor.Cyan);
        return Console.ReadLine()?.Trim();
    }

    public static void BeginAnswer() => Write("\nClaude> ", ConsoleColor.Yellow);

    public static void Write(string text) => Console.Write(text);

    public static void EndAnswer(IReadOnlyList<ChatResponseUpdate> updates)
    {
        long input = 0, output = 0;
        foreach (var usage in updates.SelectMany(u => u.Contents).OfType<UsageContent>())
        {
            input += usage.Details.InputTokenCount ?? 0;
            output += usage.Details.OutputTokenCount ?? 0;
        }

        Console.WriteLine();

        if (input + output > 0)
            WriteLine($"  [токены: вход {input}, выход {output}]", ConsoleColor.DarkGray);
    }

    public static void ToolCall(string name, AIFunctionArguments arguments)
    {
        var args = string.Join(", ", arguments.Select(a => $"{a.Key}: {JsonSerializer.Serialize(a.Value, ArgumentsJson)}"));
        WriteLine($"\n  → {name}({args})", ConsoleColor.DarkGray);
    }

    public static bool Confirm(bool destructive)
    {
        Write(destructive
            ? "  ⚠ Массовая или необратимая операция. Выполнить? [y/N] "
            : "  Инструмент изменит данные CRM. Выполнить? [y/N] ", ConsoleColor.Magenta);

        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "y" or "yes" or "д" or "да";
    }

    public static void ToolDeclined() => WriteLine("    ✗ отклонено", ConsoleColor.DarkRed);

    public static void ToolResult(object? result)
    {
        var (text, isError) = Describe(result);
        var firstLine = text.Split('\n', 2)[0].Trim();

        if (firstLine.Length > 110)
            firstLine = firstLine[..110] + "…";

        WriteLine($"    {(isError ? "✗" : "✓")} {firstLine}", isError ? ConsoleColor.DarkRed : ConsoleColor.DarkGray);
    }

    public static void Tools(IEnumerable<McpClientTool> tools)
    {
        foreach (var tool in tools)
        {
            var kind = tool.ProtocolTool.Annotations?.ReadOnlyHint == true ? "чтение" : "изменение";
            WriteLine($"  {tool.Name} ({kind}) — {tool.Description}", ConsoleColor.Gray);
        }
    }

    public static void Prompts(IEnumerable<McpClientPrompt> prompts)
    {
        foreach (var prompt in prompts)
        {
            var args = string.Join(" ", prompt.ProtocolPrompt.Arguments?.Select(a => $"{a.Name}=…") ?? []);
            WriteLine($"  /prompt {prompt.Name} {args} — {prompt.Description}", ConsoleColor.Gray);
        }
    }

    public static void Info(string message) => WriteLine(message, ConsoleColor.DarkGray);

    public static void Error(string message) => WriteLine(message, ConsoleColor.Red);

    // Результат MCP-инструмента приходит как TextContent, а ошибка — как JSON CallToolResult с isError=true
    private static (string Text, bool IsError) Describe(object? result) => result switch
    {
        null => ("(пустой ответ)", false),
        string s => (s, false),
        TextContent text => (text.Text, false),
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
