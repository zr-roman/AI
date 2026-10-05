using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Errors;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;

namespace Crm.GraphQL.Tasks;

[MutationType]
public static partial class TaskMutations
{
    [GraphQLDescription("Создаёт задачу текущему пользователю. Если указана только сделка, контакт подставится из неё.")]
    [Error<ValidationError>]
    [Error<NotFoundError>]
    public static async Task<CreateTaskPayload> CreateTaskAsync(
        string title,
        [GraphQLDescription("Срок, не раньше сегодняшнего дня")] DateOnly dueDate,
        TaskService tasks,
        ITaskByIdDataLoader taskById,
        CancellationToken cancellationToken,
        string? details = null,
        int? contactId = null,
        int? dealId = null)
    {
        var item = await tasks.CreateAsync(new NewTask(title, dueDate, details, contactId, dealId), cancellationToken);
        return new CreateTaskPayload(await taskById.LoadRequiredAsync(item.Id, cancellationToken));
    }

    [GraphQLDescription("Переносит срок, меняет название или подробности невыполненной задачи. Передавай только то, что меняется.")]
    [Error<ValidationError>]
    [Error<NotFoundError>]
    [Error<ForbiddenError>]
    public static async Task<UpdateTaskPayload> UpdateTaskAsync(
        int taskId,
        TaskService tasks,
        ITaskByIdDataLoader taskById,
        CancellationToken cancellationToken,
        string? title = null,
        [GraphQLDescription("Новый срок, не раньше сегодняшнего дня")] DateOnly? dueDate = null,
        string? details = null)
    {
        var result = await tasks.UpdateAsync(taskId, new TaskUpdate(title, dueDate, details), cancellationToken);
        return new UpdateTaskPayload(await taskById.ReloadAsync(taskId, cancellationToken), result.Changes);
    }

    [GraphQLDescription("Отмечает задачу выполненной. Результат записывается в историю контакта и сделки.")]
    [Error<ValidationError>]
    [Error<NotFoundError>]
    [Error<ForbiddenError>]
    public static async Task<CompleteTaskPayload> CompleteTaskAsync(
        int taskId,
        TaskService tasks,
        ITaskByIdDataLoader taskById,
        CancellationToken cancellationToken,
        [GraphQLDescription("Чем закончилось, например «Договор подписан»")] string? resultNote = null)
    {
        await tasks.CompleteAsync(taskId, resultNote, cancellationToken);
        return new CompleteTaskPayload(await taskById.ReloadAsync(taskId, cancellationToken));
    }
}

public sealed record CreateTaskPayload(CrmTask Task);

public sealed record UpdateTaskPayload(CrmTask Task, IReadOnlyList<string> Changes);

public sealed record CompleteTaskPayload(CrmTask Task);
