using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using SqlAnalyst.Core.Charts;

namespace SqlAnalyst.Agent;

/// <summary>
/// Guardrails на стороне агента — перехватчик вызовов инструментов (FunctionInvokingChatClient.FunctionInvoker):
/// <list type="bullet">
/// <item>модель видит только инструменты, которые сервер пометил как read-only и не выходящие во внешний мир;
///   если сервер вдруг объявит изменяющий инструмент, агент его просто не подключит;</item>
/// <item>бюджет вызовов на один вопрос — зациклившаяся или «уговорённая» данными модель не будет долбить БД;</item>
/// <item>учёт срабатываний защиты от prompt injection, чтобы показать их пользователю.</item>
/// </list>
/// </summary>
public sealed class ToolGuard(int maxCallsPerQuestion)
{
    private int _calls;

    public int InjectionWarnings { get; private set; }

    public int BlockedQueries { get; private set; }

    public static (List<AITool> Allowed, List<string> Rejected) FilterTools(IEnumerable<McpClientTool> tools)
    {
        var allowed = new List<AITool>();
        var rejected = new List<string>();

        foreach (var tool in tools)
        {
            var annotations = tool.ProtocolTool.Annotations;
            if (annotations?.ReadOnlyHint == true && annotations.OpenWorldHint != true)
            {
                allowed.Add(tool);
            }
            else
            {
                rejected.Add(tool.Name);
            }
        }

        return (allowed, rejected);
    }

    public void StartQuestion()
    {
        _calls = 0;
        InjectionWarnings = 0;
        BlockedQueries = 0;
    }

    public async ValueTask<object?> InvokeAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        AgentConsole.ToolCall(context.Function.Name, context.Arguments);

        if (++_calls > maxCallsPerQuestion)
        {
            AgentConsole.ToolDenied("исчерпан бюджет вызовов на вопрос");
            context.Terminate = true;
            return $"Лимит {maxCallsPerQuestion} вызовов инструментов на один вопрос исчерпан. Ответь по уже полученным данным или попроси пользователя уточнить вопрос.";
        }

        var isMcpReadOnly = context.Function is McpClientTool { ProtocolTool.Annotations.ReadOnlyHint: true };
        if (!isMcpReadOnly && context.Function.Name != ChartTool.Name)
        {
            AgentConsole.ToolDenied("инструмент не в списке разрешённых");
            return "Этот инструмент недоступен.";
        }

        object? result;
        try
        {
            result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);
        }
        catch (ChartSpecException ex)
        {
            result = $"Ошибка построения графика: {ex.Message}";
        }

        var (text, isError) = AgentConsole.Describe(result);

        if (text.Contains("prompt injection", StringComparison.Ordinal))
        {
            InjectionWarnings++;
        }

        if (isError && text.StartsWith("Запрос отклонён", StringComparison.Ordinal))
        {
            BlockedQueries++;
        }

        AgentConsole.ToolResult(text, isError);
        return result;
    }
}
