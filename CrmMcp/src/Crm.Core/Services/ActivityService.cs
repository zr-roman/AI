using Crm.Core.Data;
using Crm.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Crm.Core.Services;

public sealed class ActivityService(CrmDbContext db, ICurrentUser currentUser, CrmClock clock)
{
    public async Task<ActivityItem> LogAsync(NewActivity input, CancellationToken ct = default)
    {
        if (input.Type == ActivityType.StageChange)
        {
            throw new CrmValidationException("Записи о смене стадии создаются автоматически при переводе сделки.");
        }

        var subject = Text.Required(input.Subject, "Тема", 300);
        var details = Text.Optional(input.Details, "Подробности", 4000);
        if (input.ContactId is null && input.DealId is null)
        {
            throw new CrmValidationException("Привяжи активность к контакту (contactId) или к сделке (dealId).");
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

        var now = clock.UtcNow;
        var occurredAt = (input.OccurredAt ?? now).ToUniversalTime();
        if (occurredAt > now.AddHours(1))
        {
            throw new CrmValidationException(
                "Активность фиксирует уже случившееся событие и не может быть в будущем. Запланированные дела оформляй задачей.");
        }

        var activity = new Activity
        {
            Type = input.Type,
            Subject = subject,
            Details = details,
            OccurredAt = occurredAt,
            ContactId = contactId,
            DealId = input.DealId,
            AuthorId = currentUser.Id,
        };

        db.Activities.Add(activity);
        await db.SaveChangesAsync(ct);

        return await db.Activities.AsNoTracking()
            .Where(a => a.Id == activity.Id)
            .Select(Projections.ToActivityItem)
            .FirstAsync(ct);
    }
}
