using System.Diagnostics;
using System.Text.Json;
using Crm.Core.Domain;
using Crm.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Crm.Mcp;

internal static class CrmMcpFilters
{
    /// <summary>
    /// Фильтр вызова инструментов:
    /// 1) бизнес-ошибки (CrmException) отдаёт модели как результат с isError=true и понятным текстом —
    ///    по умолчанию SDK скрывает текст исключений и модель не поймёт, что исправить;
    /// 2) пишет аудит каждого вызова: кто, какой инструмент, с какими аргументами, успех или ошибка.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> CallTool(string channel) =>
        next => async (context, cancellationToken) =>
        {
            var services = context.Services!;
            var stopwatch = Stopwatch.StartNew();
            string? error = null;

            try
            {
                var result = await next(context, cancellationToken);
                if (result.IsError == true)
                {
                    error = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "Tool error";
                }

                return result;
            }
            catch (CrmException ex)
            {
                error = ex.Message;
                return ToolError(ex.Message);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            {
                // Модель пропустила обязательный аргумент или передала значение не того типа
                // (например, несуществующую стадию) — дадим ей шанс исправиться
                error = ex.Message;
                return ToolError($"Некорректные аргументы: {ex.Message} Сверься со схемой инструмента (типы и допустимые значения) и повтори вызов.");
            }
            catch (McpProtocolException ex)
            {
                // Например, неизвестный инструмент: SDK сам вернёт JSON-RPC ошибку
                error = ex.Message;
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Неожиданная ошибка: детали только в лог и аудит, модели SDK вернёт обезличенное сообщение
                error = $"{ex.GetType().Name}: {ex.Message}";
                services.GetRequiredService<ILoggerFactory>().CreateLogger("Crm.Mcp")
                    .LogError(ex, "Tool {Tool} failed", context.Params?.Name);
                throw;
            }
            finally
            {
                var currentUser = services.GetRequiredService<ICurrentUser>();
                var arguments = context.Params?.Arguments is { Count: > 0 } args ? JsonSerializer.Serialize(args) : null;

                // CancellationToken.None: аудит пишем даже если клиент уже отключился
                await services.GetRequiredService<AuditLog>().WriteAsync(new AuditEntry
                {
                    OccurredAt = services.GetRequiredService<TimeProvider>().GetUtcNow(),
                    UserId = currentUser.IsAuthenticated ? currentUser.Id : null,
                    Channel = channel,
                    Operation = context.Params?.Name ?? "?",
                    Arguments = arguments,
                    Succeeded = error is null,
                    Error = error is null ? null : Truncate(error, 2000),
                    DurationMs = (int)stopwatch.ElapsedMilliseconds,
                }, CancellationToken.None);
            }
        };

    /// <summary>Для ресурсов «не найдено» — это протокольная ошибка ResourceNotFound, а не сбой сервера.</summary>
    public static McpRequestFilter<ReadResourceRequestParams, ReadResourceResult> ReadResource() =>
        next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (CrmNotFoundException ex)
            {
                throw new McpProtocolException(ex.Message, McpErrorCode.ResourceNotFound);
            }
            catch (CrmException ex)
            {
                throw new McpProtocolException(ex.Message, McpErrorCode.InvalidParams);
            }
        };

    public static McpRequestFilter<GetPromptRequestParams, GetPromptResult> GetPrompt() =>
        next => async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (CrmException ex)
            {
                throw new McpProtocolException(ex.Message, McpErrorCode.InvalidParams);
            }
        };

    private static CallToolResult ToolError(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
