using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Crm.Agent;

/// <summary>
/// Перехватчик вызовов инструментов (FunctionInvokingChatClient.FunctionInvoker).
/// Читающие инструменты выполняются сразу, изменяющие — только после подтверждения человеком.
/// Что читает, а что меняет, агент узнаёт из аннотаций, которые сервер CRM отдаёт в tools/list.
/// </summary>
internal static class ToolApproval
{
    public static async ValueTask<object?> InvokeAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        var annotations = (context.Function as McpClientTool)?.ProtocolTool.Annotations;
        AgentConsole.ToolCall(context.Function.Name, context.Arguments);

        var readOnly = annotations?.ReadOnlyHint == true;

        // По спецификации MCP инструмент без readOnly считается потенциально разрушающим, пока сервер не сказал обратное
        var destructive = !readOnly && annotations?.DestructiveHint != false;

        if (!readOnly && !AgentConsole.Confirm(destructive))
        {
            AgentConsole.ToolDeclined();
            return "Пользователь отклонил этот вызов. Не повторяй его, пока пользователь не попросит снова.";
        }

        var result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);
        AgentConsole.ToolResult(result);
        return result;
    }
}
