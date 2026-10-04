using Crm.Core.Data;
using Crm.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Crm.Core.Services;

public sealed class TaskService(CrmDbContext db, ICurrentUser currentUser, CrmClock clock)
{
    public async Task<TaskItem> CreateAsync(NewTask input, CancellationToken ct = default)
    {
        var title = Text.Required(input.Title, "Название задачи", 300);
        var details = Text.Optional(input.Details, "Подробности", 4000);
        if (input.DueDate < clock.Today)
        {
            throw new CrmValidationException($"Срок задачи ({input.DueDate:yyyy-MM-dd}) уже прошёл. Сегодня {clock.Today:yyyy-MM-dd}.");
        }

        var contactId = input.ContactId;
        if (input.DealId is { } dealId)
        {
            var deal = await db.Deals.AsNoTracking()
                .Where(d => d.Id == dealId)
                .Select(d => new { d.ContactId })
                .FirstOrDefaultAsync(ct)
                ?? throw new CrmNotFoundException($"Сделка #{dealId} не найдена.");

            contactId ??= deal.ContactId;
        }

        if (input.ContactId is { } id && !await db.Contacts.AnyAsync(c => c.Id == id, ct))
        {
            throw new CrmNotFoundException($"Контакт #{id} не найден.");
        }

        var task = new CrmTask
        {
            Title = title,
            Details = details,
            DueDate = input.DueDate,
            AssigneeId = currentUser.Id,
            ContactId = contactId,
            DealId = input.DealId,
            CreatedAt = clock.UtcNow,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return await GetItemAsync(task.Id, ct);
    }

    public async Task<IReadOnlyList<TaskItem>> ListMineAsync(TaskFilter filter = TaskFilter.Open, CancellationToken ct = default)
    {
        var me = currentUser.Id;
        var today = clock.Today;
        var weekEnd = today.AddDays(7);

        var tasks = db.Tasks.AsNoTracking().Where(t => t.AssigneeId == me && !t.IsCompleted);
        tasks = filter switch
        {
            TaskFilter.Overdue => tasks.Where(t => t.DueDate < today),
            TaskFilter.Today => tasks.Where(t => t.DueDate == today),
            TaskFilter.Week => tasks.Where(t => t.DueDate <= weekEnd),
            _ => tasks,
        };

        return await tasks
            .OrderBy(t => t.DueDate).ThenBy(t => t.Id)
            .Take(100)
            .Select(Projections.ToTaskItem)
            .ToListAsync(ct);
    }

    public async Task<TaskItem> CompleteAsync(int taskId, string? resultNote, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new CrmNotFoundException($"Задача #{taskId} не найдена.");

        currentUser.EnsureCanModify(task.AssigneeId, $"задача #{task.Id}");

        if (task.IsCompleted)
        {
            throw new CrmValidationException($"Задача #{task.Id} уже выполнена.");
        }

        var now = clock.UtcNow;
        task.IsCompleted = true;
        task.CompletedAt = now;

        if (Text.Optional(resultNote, "Результат", 2000) is { } note)
        {
            task.Details = Text.Truncate(task.Details is null ? $"Результат: {note}" : $"{task.Details}\nРезультат: {note}", 4000);

            // Результат задачи попадает и в историю клиента/сделки
            if (task.ContactId is not null || task.DealId is not null)
            {
                db.Activities.Add(new Activity
                {
                    Type = ActivityType.Note,
                    Subject = Text.Truncate($"Выполнена задача: {task.Title}", 300),
                    Details = note,
                    OccurredAt = now,
                    ContactId = task.ContactId,
                    DealId = task.DealId,
                    AuthorId = currentUser.Id,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetItemAsync(task.Id, ct);
    }

    private Task<TaskItem> GetItemAsync(int taskId, CancellationToken ct) =>
        db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(Projections.ToTaskItem)
            .FirstAsync(ct);
}
