using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Errors;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;

namespace Crm.GraphQL.Activities;

[MutationType]
public static partial class ActivityMutations
{
    [GraphQLDescription(
        "Записывает в историю звонок, письмо, встречу или заметку по контакту и/или сделке. " +
        "Если указана только сделка, контакт подставится из неё. Будущие дела оформляй задачей (createTask).")]
    [Error<ValidationError>]
    [Error<NotFoundError>]
    public static async Task<LogActivityPayload> LogActivityAsync(
        LoggableActivityType type,
        [GraphQLDescription("Краткий итог одной строкой")] string subject,
        ActivityService activities,
        IActivityByIdDataLoader activityById,
        CancellationToken cancellationToken,
        [GraphQLDescription("Подробности: договорённости, возражения, следующие шаги")] string? details = null,
        int? contactId = null,
        int? dealId = null,
        [GraphQLDescription("Когда это произошло. По умолчанию — сейчас")] DateTimeOffset? occurredAt = null)
    {
        var item = await activities.LogAsync(
            new NewActivity(type.ToActivityType(), subject, details, contactId, dealId, occurredAt),
            cancellationToken);

        return new LogActivityPayload(await activityById.LoadRequiredAsync(item.Id, cancellationToken));
    }
}

public sealed record LogActivityPayload(Activity Activity);

/// <summary>
/// Типы, которые можно записать вручную. STAGE_CHANGE пишет система при смене стадии, поэтому его нет в схеме
/// (так же сделано в MCP-инструменте log_activity).
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
