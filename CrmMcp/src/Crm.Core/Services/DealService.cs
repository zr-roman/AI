using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Crm.Core.Data;
using Crm.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crm.Core.Services;

public sealed class DealService(CrmDbContext db, ICurrentUser currentUser, CrmClock clock, IOptions<CrmOptions> options)
{
    private const decimal MaxAmount = 1_000_000_000_000m;

    public async Task<IReadOnlyList<DealListItem>> ListAsync(DealFilter filter, int limit = 20, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var deals = db.Deals.AsNoTracking();

        deals = filter.Stage is { } stage
            ? deals.Where(d => d.Stage == stage)
            : deals.Where(Projections.IsOpenDeal);

        if (filter.OnlyMine)
        {
            var me = currentUser.Id;
            deals = deals.Where(d => d.OwnerId == me);
        }

        if (Text.Normalize(filter.Query) is { } term)
        {
            var pattern = Text.ContainsPattern(term);
            deals = deals.Where(d =>
                EF.Functions.ILike(d.Title, pattern) ||
                (d.Company != null && EF.Functions.ILike(d.Company.Name, pattern)));
        }

        return await deals
            .OrderByDescending(d => d.Amount)
            .Take(limit)
            .Select(Projections.ToDealListItem)
            .ToListAsync(ct);
    }

    public async Task<DealCard> GetCardAsync(int dealId, CancellationToken ct = default)
    {
        var deal = await db.Deals.AsNoTracking()
            .Where(d => d.Id == dealId)
            .Select(d => new { d.LostReason, d.CreatedAt, d.ClosedAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new CrmNotFoundException($"Сделка #{dealId} не найдена.");

        var item = await GetListItemAsync(dealId, ct);

        var activities = await db.Activities.AsNoTracking()
            .Where(a => a.DealId == dealId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(15)
            .Select(Projections.ToActivityItem)
            .ToListAsync(ct);

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DealId == dealId && !t.IsCompleted)
            .OrderBy(t => t.DueDate)
            .Select(Projections.ToTaskItem)
            .ToListAsync(ct);

        return new DealCard(item, deal.LostReason, deal.CreatedAt, deal.ClosedAt, activities, tasks);
    }

    public async Task<DealListItem> CreateAsync(NewDeal input, CancellationToken ct = default)
    {
        var title = Text.Required(input.Title, "Название сделки", 300);
        ValidateAmount(input.Amount);

        if (input.Stage.IsClosed())
        {
            throw new CrmValidationException(
                "Новую сделку можно создать только в открытой стадии: Lead, Qualified, Proposal или Negotiation.");
        }

        var companyId = input.CompanyId;
        if (input.ContactId is { } contactId)
        {
            var contact = await db.Contacts.AsNoTracking()
                .Where(c => c.Id == contactId)
                .Select(c => new { c.CompanyId })
                .FirstOrDefaultAsync(ct)
                ?? throw new CrmNotFoundException($"Контакт #{contactId} не найден.");

            // Компанию берём из контакта, если её не указали явно
            companyId ??= contact.CompanyId;
        }

        if (companyId is { } id && !await db.Companies.AnyAsync(c => c.Id == id, ct))
        {
            throw new CrmNotFoundException($"Компания #{id} не найдена.");
        }

        var now = clock.UtcNow;
        var deal = new Deal
        {
            Title = title,
            Amount = input.Amount,
            Stage = input.Stage,
            ContactId = input.ContactId,
            CompanyId = companyId,
            ExpectedCloseDate = input.ExpectedCloseDate,
            OwnerId = currentUser.Id,
            CreatedAt = now,
            StageChangedAt = now,
        };

        db.Deals.Add(deal);
        await db.SaveChangesAsync(ct);
        return await GetListItemAsync(deal.Id, ct);
    }

    public async Task<StageChangeResult> MoveStageAsync(int dealId, DealStage newStage, string? lostReason, CancellationToken ct = default)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId, ct)
            ?? throw new CrmNotFoundException($"Сделка #{dealId} не найдена.");

        currentUser.EnsureCanModify(deal.OwnerId, $"сделка #{deal.Id}");

        if (deal.Stage == newStage)
        {
            throw new CrmValidationException($"Сделка #{deal.Id} уже в стадии {newStage}.");
        }

        var reason = Text.Optional(lostReason, "Причина проигрыша", 500);
        if (newStage == DealStage.Lost && reason is null)
        {
            throw new CrmValidationException("Для перевода в Lost укажи причину проигрыша (lostReason).");
        }

        var previous = deal.Stage;
        ApplyStage(deal, newStage, reason);
        await db.SaveChangesAsync(ct);

        return new StageChangeResult(await GetListItemAsync(deal.Id, ct), previous);
    }

    /// <summary>
    /// Меняет название, сумму и/или ожидаемую дату закрытия открытой сделки. Стадию меняет <see cref="MoveStageAsync"/>.
    /// Что изменилось, записывается в историю сделки.
    /// </summary>
    public async Task<DealUpdateResult> UpdateAsync(int dealId, DealUpdate input, CancellationToken ct = default)
    {
        if (input.Title is null && input.Amount is null && input.ExpectedCloseDate is null && !input.ClearExpectedCloseDate)
        {
            throw new CrmValidationException("Укажи, что изменить: title, amount, expectedCloseDate или clearExpectedCloseDate.");
        }

        if (input.ExpectedCloseDate is not null && input.ClearExpectedCloseDate)
        {
            throw new CrmValidationException("Нельзя одновременно задать expectedCloseDate и очистить её (clearExpectedCloseDate).");
        }

        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId, ct)
            ?? throw new CrmNotFoundException($"Сделка #{dealId} не найдена.");

        currentUser.EnsureCanModify(deal.OwnerId, $"сделка #{deal.Id}");

        if (deal.Stage.IsClosed())
        {
            throw new CrmValidationException(
                $"Сделка #{deal.Id} закрыта ({deal.Stage}). Чтобы изменить её, сначала переоткрой сделку сменой стадии.");
        }

        var changes = new List<string>();

        if (input.Title is not null)
        {
            var title = Text.Required(input.Title, "Название сделки", 300);
            if (title != deal.Title)
            {
                changes.Add($"название: «{deal.Title}» → «{title}»");
                deal.Title = title;
            }
        }

        if (input.Amount is { } amount)
        {
            ValidateAmount(amount);
            if (amount != deal.Amount)
            {
                changes.Add($"сумма: {Rubles(deal.Amount)} → {Rubles(amount)}");
                deal.Amount = amount;
            }
        }

        var closeDate = input.ClearExpectedCloseDate ? null : input.ExpectedCloseDate ?? deal.ExpectedCloseDate;
        if (closeDate != deal.ExpectedCloseDate)
        {
            changes.Add($"ожидаемое закрытие: {DateText(deal.ExpectedCloseDate)} → {DateText(closeDate)}");
            deal.ExpectedCloseDate = closeDate;
        }

        if (changes.Count == 0)
        {
            throw new CrmValidationException($"У сделки #{deal.Id} уже такие значения, менять нечего.");
        }

        db.Activities.Add(new Activity
        {
            Type = ActivityType.Note,
            Subject = "Изменены данные сделки",
            Details = string.Join("; ", changes),
            OccurredAt = clock.UtcNow,
            DealId = deal.Id,
            ContactId = deal.ContactId,
            AuthorId = currentUser.Id,
        });

        await db.SaveChangesAsync(ct);
        return new DealUpdateResult(await GetListItemAsync(deal.Id, ct), changes);
    }

    public async Task<PipelineSummary> GetPipelineSummaryAsync(bool onlyMine = false, CancellationToken ct = default)
    {
        var deals = db.Deals.AsNoTracking();
        if (onlyMine)
        {
            var me = currentUser.Id;
            deals = deals.Where(d => d.OwnerId == me);
        }

        var byStage = await deals
            .Where(Projections.IsOpenDeal)
            .GroupBy(d => d.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count(), Amount = g.Sum(d => d.Amount) })
            .ToListAsync(ct);

        var stages = Enum.GetValues<DealStage>()
            .Where(s => !s.IsClosed())
            .Select(s =>
            {
                var row = byStage.FirstOrDefault(r => r.Stage == s);
                var amount = row?.Amount ?? 0m;
                return new PipelineStage(s, row?.Count ?? 0, amount, amount * s.Probability());
            })
            .ToList();

        var monthStart = clock.StartOfMonthUtc;
        var wonThisMonth = deals.Where(d => d.Stage == DealStage.Won && d.ClosedAt >= monthStart);
        var wonCount = await wonThisMonth.CountAsync(ct);
        var wonAmount = await wonThisMonth.SumAsync(d => d.Amount, ct);

        var staleDays = options.Value.StaleDealDays;
        var staleCount = await StaleDeals(deals, staleDays).CountAsync(ct);

        return new PipelineSummary(
            stages,
            stages.Sum(s => s.Count),
            stages.Sum(s => s.Amount),
            stages.Sum(s => s.WeightedAmount),
            wonCount,
            wonAmount,
            staleCount,
            staleDays);
    }

    /// <summary>Шаг 1 массового закрытия: список зависших сделок и токен подтверждения. Ничего не меняет.</summary>
    public async Task<StaleDealsPreview> PreviewStaleDealsAsync(int inactiveDays, CancellationToken ct = default)
    {
        currentUser.EnsureAdmin("массовое закрытие сделок");
        ValidateInactiveDays(inactiveDays);

        var items = await StaleDeals(db.Deals.AsNoTracking(), inactiveDays)
            .OrderBy(d => d.StageChangedAt)
            .Select(Projections.ToDealListItem)
            .ToListAsync(ct);

        return new StaleDealsPreview(inactiveDays, items, ConfirmationToken(inactiveDays, items.Select(d => d.Id)));
    }

    /// <summary>
    /// Шаг 2: переводит зависшие сделки в Lost. Токен — хэш набора сделок из превью: если с тех пор набор изменился,
    /// операция отклоняется. Так агент не может «проскочить» превью или закрыть не то, что видел пользователь.
    /// </summary>
    public async Task<IReadOnlyList<DealListItem>> CloseStaleDealsAsync(int inactiveDays, string confirmationToken, CancellationToken ct = default)
    {
        currentUser.EnsureAdmin("массовое закрытие сделок");
        ValidateInactiveDays(inactiveDays);

        var deals = await StaleDeals(db.Deals, inactiveDays).ToListAsync(ct);
        var expectedToken = ConfirmationToken(inactiveDays, deals.Select(d => d.Id));
        if (!string.Equals(expectedToken, confirmationToken.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmConflictException(
                "Токен подтверждения не подходит: превью не запрашивалось или список зависших сделок с тех пор изменился. Запроси превью заново.");
        }

        var reason = $"Нет активности более {inactiveDays} дн. (массовое закрытие)";
        foreach (var deal in deals)
        {
            ApplyStage(deal, DealStage.Lost, reason);
        }

        await db.SaveChangesAsync(ct);

        var ids = deals.Select(d => d.Id).ToList();
        return await db.Deals.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .OrderBy(d => d.Id)
            .Select(Projections.ToDealListItem)
            .ToListAsync(ct);
    }

    private Task<DealListItem> GetListItemAsync(int dealId, CancellationToken ct) =>
        db.Deals.AsNoTracking()
            .Where(d => d.Id == dealId)
            .Select(Projections.ToDealListItem)
            .FirstAsync(ct);

    /// <summary>Открытые сделки, у которых давно не менялась стадия и не было активностей.</summary>
    private IQueryable<Deal> StaleDeals(IQueryable<Deal> deals, int inactiveDays)
    {
        var cutoff = clock.UtcNow.AddDays(-inactiveDays);
        return deals
            .Where(Projections.IsOpenDeal)
            .Where(d => d.StageChangedAt < cutoff && !d.Activities.Any(a => a.OccurredAt >= cutoff));
    }

    private void ApplyStage(Deal deal, DealStage newStage, string? lostReason)
    {
        var now = clock.UtcNow;
        var previous = deal.Stage;

        deal.Stage = newStage;
        deal.StageChangedAt = now;
        deal.ClosedAt = newStage.IsClosed() ? now : null;
        deal.LostReason = newStage == DealStage.Lost ? lostReason : null;

        // Смена стадии всегда попадает в историю — и для людей, и для разбора воронки агентом
        db.Activities.Add(new Activity
        {
            Type = ActivityType.StageChange,
            Subject = $"Стадия: {previous} → {newStage}",
            Details = newStage == DealStage.Lost ? $"Причина: {lostReason}" : null,
            OccurredAt = now,
            DealId = deal.Id,
            ContactId = deal.ContactId,
            AuthorId = currentUser.Id,
        });
    }

    private string ConfirmationToken(int inactiveDays, IEnumerable<int> dealIds)
    {
        var payload = $"{currentUser.Id}|{inactiveDays}|{string.Join(',', dealIds.Order())}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..12];
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount is < 0 or > MaxAmount)
        {
            throw new CrmValidationException($"Сумма сделки должна быть от 0 до {MaxAmount:N0} ₽.");
        }
    }

    private static readonly NumberFormatInfo RublesFormat = new() { NumberGroupSeparator = " ", NumberDecimalSeparator = "," };

    // Тексты для истории сделки. Формат совпадает с CrmFormatter, но Crm.Core не зависит от Crm.Mcp
    private static string Rubles(decimal amount) => amount.ToString("#,0.##", RublesFormat) + " ₽";

    private static string DateText(DateOnly? date) =>
        date is { } d ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "не указана";

    private static void ValidateInactiveDays(int inactiveDays)
    {
        if (inactiveDays is < 7 or > 365)
        {
            throw new CrmValidationException("inactiveDays должен быть от 7 до 365.");
        }
    }
}
