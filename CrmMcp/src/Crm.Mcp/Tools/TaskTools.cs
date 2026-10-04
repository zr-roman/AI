using System.ComponentModel;
using Crm.Core.Services;
using Crm.Mcp.Formatting;
using ModelContextProtocol.Server;

namespace Crm.Mcp.Tools;

[McpServerToolType]
public sealed class TaskTools(TaskService tasks, CrmFormatter format)
{
    [McpServerTool(Name = "create_task", Title = "Создать задачу", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Создаёт задачу (следующий шаг) для текущего пользователя, при необходимости привязанную к контакту или сделке.")]
    public async Task<string> CreateTask(
        [Description("Что сделать, например «Отправить договор»")] string title,
        [Description("Срок, YYYY-MM-DD. Не раньше сегодняшнего дня")] DateOnly dueDate,
        [Description("Подробности")] string? details = null,
        [Description("ID контакта")] int? contactId = null,
        [Description("ID сделки. Если контакт не указан, он подставится из сделки")] int? dealId = null,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.CreateAsync(new NewTask(title, dueDate, details, contactId, dealId), cancellationToken);
        return format.TaskCreated(task);
    }

    [McpServerTool(Name = "list_my_tasks", Title = "Мои задачи", ReadOnly = true, OpenWorld = false)]
    [Description("Невыполненные задачи текущего пользователя. Open — все, Overdue — просроченные, Today — на сегодня, Week — на 7 дней вперёд вместе с просроченными.")]
    public async Task<string> ListMyTasks(
        [Description("Какие задачи показать")] TaskFilter filter = TaskFilter.Open,
        CancellationToken cancellationToken = default)
    {
        var found = await tasks.ListMineAsync(filter, cancellationToken);
        return format.TaskList(found, filter);
    }

    [McpServerTool(Name = "complete_task", Title = "Закрыть задачу", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Отмечает задачу выполненной. Результат, если указан, сохраняется в задаче и в истории контакта или сделки.")]
    public async Task<string> CompleteTask(
        [Description("ID задачи")] int taskId,
        [Description("Чем закончилось, например «Договор отправлен, ждём подписи до пятницы»")] string? resultNote = null,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.CompleteAsync(taskId, resultNote, cancellationToken);
        return format.TaskCompleted(task, resultSaved: !string.IsNullOrWhiteSpace(resultNote));
    }
}
