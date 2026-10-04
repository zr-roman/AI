using System.ComponentModel;
using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.Mcp.Formatting;
using ModelContextProtocol.Server;

namespace Crm.Mcp.Tools;

[McpServerToolType]
public sealed class ActivityTools(ActivityService activities, CrmFormatter format)
{
    [McpServerTool(Name = "log_activity", Title = "Записать активность", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Записывает в историю звонок, письмо, встречу или заметку по контакту и/или сделке. Вызывай после каждого значимого общения с клиентом. Для будущих дел используй create_task.")]
    public async Task<string> LogActivity(
        [Description("Тип активности")] LoggableActivityType type,
        [Description("Краткий итог одной строкой")] string subject,
        [Description("Подробности: договорённости, возражения, следующие шаги")] string? details = null,
        [Description("ID контакта")] int? contactId = null,
        [Description("ID сделки. Если контакт не указан, он подставится из сделки")] int? dealId = null,
        [Description("Когда это произошло, ISO 8601 с часовым поясом. По умолчанию — сейчас")] DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        var item = await activities.LogAsync(
            new NewActivity(type.ToActivityType(), subject, details, contactId, dealId, occurredAt),
            cancellationToken);

        return format.ActivityLogged(item);
    }
}

/// <summary>
/// Отдельный enum для схемы инструмента: модель видит только те типы, которые ей разрешено создавать
/// (StageChange пишет система при смене стадии).
/// </summary>
public enum LoggableActivityType
{
    Call,
    Email,
    Meeting,
    Note,
}

internal static class LoggableActivityTypeExtensions
{
    public static ActivityType ToActivityType(this LoggableActivityType type) => type switch
    {
        LoggableActivityType.Call => ActivityType.Call,
        LoggableActivityType.Email => ActivityType.Email,
        LoggableActivityType.Meeting => ActivityType.Meeting,
        LoggableActivityType.Note => ActivityType.Note,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный тип активности."),
    };
}
