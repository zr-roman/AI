using System.Globalization;
using System.Text;
using Crm.Core.Domain;
using Crm.Core.Services;

namespace Crm.Mcp.Formatting;

/// <summary>
/// Превращает модели CRM в компактный текст для LLM. Модели проще читать короткий Markdown
/// с явными идентификаторами (#12), чем сырой JSON, а токенов уходит меньше.
/// </summary>
public sealed class CrmFormatter(CrmClock clock)
{
    private static readonly NumberFormatInfo RublesFormat = new() { NumberGroupSeparator = " ", NumberDecimalSeparator = "," };

    public static string Money(decimal amount) => amount.ToString("#,0", RublesFormat) + " ₽";

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public string DateTime(DateTimeOffset value) =>
        clock.ToLocal(value).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Stage(DealStage stage) => $"{stage} ({stage.DisplayName()})";

    // ---------- Контакты ----------

    public string ContactList(IReadOnlyList<ContactListItem> contacts, string? query)
    {
        if (contacts.Count == 0)
        {
            return string.IsNullOrWhiteSpace(query)
                ? "Контактов пока нет."
                : $"По запросу «{query}» контакты не найдены.";
        }

        var sb = new StringBuilder($"Найдено контактов: {contacts.Count}").AppendLine();
        foreach (var contact in contacts)
        {
            sb.Append("- ").AppendLine(ContactLine(contact));
        }

        return sb.ToString();
    }

    public string ContactCreated(ContactCreated created)
    {
        var c = created.Contact;
        var company = c.CompanyName is null
            ? ""
            : created.CompanyCreated
                ? $" Создана новая компания {c.CompanyName} (#{c.CompanyId})."
                : $" Привязан к существующей компании {c.CompanyName} (#{c.CompanyId}).";

        return $"Создан контакт #{c.Id} {c.FullName}.{company}";
    }

    public string ContactCard(ContactCard card)
    {
        var c = card.Contact;
        var sb = new StringBuilder();
        sb.AppendLine($"## Контакт #{c.Id}: {c.FullName}");
        AppendField(sb, "Должность", c.Position);
        AppendField(sb, "Компания", c.CompanyName is null ? null : $"{c.CompanyName} (#{c.CompanyId})");
        AppendField(sb, "Email", c.Email);
        AppendField(sb, "Телефон", c.Phone);
        AppendField(sb, "Ответственный", c.OwnerName);
        AppendField(sb, "В CRM с", DateTime(c.CreatedAt));

        sb.AppendLine().AppendLine($"### Открытые сделки ({card.OpenDeals.Count})");
        AppendLines(sb, card.OpenDeals.Select(DealLine), "нет");

        sb.AppendLine().AppendLine("### Последние активности");
        AppendLines(sb, card.RecentActivities.Select(a => ActivityLine(a)), "нет");

        sb.AppendLine().AppendLine($"### Открытые задачи ({card.OpenTasks.Count})");
        AppendLines(sb, card.OpenTasks.Select(TaskLine), "нет");

        return sb.ToString();
    }

    // ---------- Сделки ----------

    public string DealList(IReadOnlyList<DealListItem> deals, DealFilter filter)
    {
        var scope = filter.Stage is { } stage ? $"в стадии {Stage(stage)}" : "открытых";
        if (filter.OnlyMine)
        {
            scope += ", только мои";
        }
        else if (!string.IsNullOrWhiteSpace(filter.Owner))
        {
            scope += $", ответственный: {filter.Owner.Trim()}";
        }

        if (deals.Count == 0)
        {
            return $"Сделок ({scope}) не найдено.";
        }

        var sb = new StringBuilder($"Сделок ({scope}): {deals.Count}, на сумму {Money(deals.Sum(d => d.Amount))}").AppendLine();
        foreach (var deal in deals)
        {
            sb.Append("- ").AppendLine(DealLine(deal));
        }

        return sb.ToString();
    }

    public string DealCard(DealCard card)
    {
        var d = card.Deal;
        var sb = new StringBuilder();
        sb.AppendLine($"## Сделка #{d.Id}: «{d.Title}»");
        AppendField(sb, "Сумма", Money(d.Amount));
        AppendField(sb, "Стадия", $"{Stage(d.Stage)}, с {DateTime(d.StageChangedAt)}");
        AppendField(sb, "Компания", d.CompanyName is null ? null : $"{d.CompanyName} (#{d.CompanyId})");
        AppendField(sb, "Контакт", d.ContactName is null ? null : $"{d.ContactName} (#{d.ContactId})");
        AppendField(sb, "Ответственный", d.OwnerName);
        AppendField(sb, "Ожидаемое закрытие", d.ExpectedCloseDate is { } date ? Date(date) : null);
        AppendField(sb, "Закрыта", card.ClosedAt is { } closedAt ? DateTime(closedAt) : null);
        AppendField(sb, "Причина проигрыша", card.LostReason);
        AppendField(sb, "Создана", DateTime(card.CreatedAt));

        sb.AppendLine().AppendLine("### История");
        AppendLines(sb, card.Activities.Select(a => ActivityLine(a, includeDeal: false)), "нет");

        sb.AppendLine().AppendLine($"### Открытые задачи ({card.OpenTasks.Count})");
        AppendLines(sb, card.OpenTasks.Select(TaskLine), "нет");

        return sb.ToString();
    }

    public string DealCreated(DealListItem deal) => $"Создана сделка: {DealLine(deal)}";

    public string DealUpdated(DealUpdateResult result) =>
        $"Сделка #{result.Deal.Id} «{result.Deal.Title}» изменена: {string.Join("; ", result.Changes)}. Изменения записаны в историю.{Environment.NewLine}Сейчас: {DealLine(result.Deal)}";

    public string StageChanged(StageChangeResult result) =>
        $"Сделка #{result.Deal.Id} «{result.Deal.Title}» переведена: {Stage(result.PreviousStage)} → {Stage(result.Deal.Stage)}. Смена стадии записана в историю.";

    public string Pipeline(PipelineSummary summary, bool onlyMine)
    {
        var sb = new StringBuilder();
        sb.AppendLine(onlyMine ? "## Моя воронка" : "## Воронка отдела");
        sb.AppendLine("| Стадия | Сделок | Сумма | Взвешенно |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var stage in summary.Stages)
        {
            sb.AppendLine($"| {Stage(stage.Stage)} | {stage.Count} | {Money(stage.Amount)} | {Money(stage.WeightedAmount)} |");
        }

        sb.AppendLine();
        sb.AppendLine($"Открыто: {summary.OpenCount} сделок на {Money(summary.OpenAmount)}; взвешенный прогноз {Money(summary.WeightedForecast)}.");
        sb.AppendLine($"Выиграно в этом месяце: {summary.WonThisMonthCount} на {Money(summary.WonThisMonthAmount)}.");
        sb.AppendLine($"Без движения больше {summary.StaleDays} дн.: {summary.StaleCount}.");
        return sb.ToString();
    }

    public string StaleDealList(StaleDealList list)
    {
        var scope = list.OnlyMine ? ", только мои" : list.Owner is null ? "" : $", ответственный: {list.Owner}";
        if (list.TotalCount == 0)
        {
            return $"Открытых сделок без движения дольше {list.InactiveDays} дн.{scope} нет.";
        }

        var shown = list.Deals.Count < list.TotalCount ? $", показаны первые {list.Deals.Count}" : "";
        var sb = new StringBuilder();
        sb.AppendLine($"Открытые сделки без движения дольше {list.InactiveDays} дн.{scope}: {list.TotalCount}{shown}, на сумму {Money(list.Deals.Sum(d => d.Deal.Amount))}");
        foreach (var item in list.Deals)
        {
            sb.Append("- ").AppendLine($"{DealLine(item.Deal)} · без движения {item.IdleDays} дн., последнее — {DateTime(item.LastMovementAt)}");
        }

        sb.AppendLine();
        sb.AppendLine("Движение — это смена стадии или запись в истории (звонок, письмо, встреча, заметка). Задачи движением не считаются.");
        return sb.ToString();
    }

    public string StaleDealsPreview(StaleDealsPreview preview)
    {
        if (preview.Deals.Count == 0)
        {
            return $"Сделок без движения дольше {preview.InactiveDays} дн. нет — закрывать нечего.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"ПРЕВЬЮ (ничего не изменено). Открытые сделки без движения дольше {preview.InactiveDays} дн.: {preview.Deals.Count}, на {Money(preview.Deals.Sum(d => d.Amount))}");
        foreach (var deal in preview.Deals)
        {
            sb.Append("- ").AppendLine(DealLine(deal));
        }

        sb.AppendLine();
        sb.AppendLine($"Токен подтверждения: {preview.ConfirmationToken}");
        sb.AppendLine("Покажи список пользователю. Только после его явного согласия вызови close_stale_deals ещё раз с тем же inactiveDays и этим confirmationToken — сделки будут переведены в Lost.");
        return sb.ToString();
    }

    public string StaleDealsClosed(IReadOnlyList<DealListItem> closed)
    {
        if (closed.Count == 0)
        {
            return "Закрывать было нечего.";
        }

        var sb = new StringBuilder($"Переведено в Lost сделок: {closed.Count}").AppendLine();
        foreach (var deal in closed)
        {
            sb.Append("- ").AppendLine($"#{deal.Id} «{deal.Title}» ({Money(deal.Amount)}, {deal.OwnerName})");
        }

        return sb.ToString();
    }

    public static string DealStagesReference()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Стадии воронки продаж");
        sb.AppendLine();
        sb.AppendLine("| Значение | Название | Вероятность | Смысл |");
        sb.AppendLine("|---|---|---:|---|");
        foreach (var stage in Enum.GetValues<DealStage>())
        {
            sb.AppendLine($"| {stage} | {stage.DisplayName()} | {stage.Probability() * 100:0}% | {stage.Description()} |");
        }

        sb.AppendLine();
        sb.AppendLine("Обычный путь: Lead → Qualified → Proposal → Negotiation → Won.");
        sb.AppendLine("В Lost можно перевести из любой открытой стадии, причина обязательна.");
        sb.AppendLine("Закрытую сделку можно переоткрыть, переведя в открытую стадию.");
        return sb.ToString();
    }

    // ---------- Активности и задачи ----------

    public string ActivityLogged(ActivityItem activity) =>
        $"Записано в историю: {ActivityLine(activity)}";

    public string TaskList(IReadOnlyList<TaskItem> tasks, TaskFilter filter)
    {
        var title = filter switch
        {
            TaskFilter.Overdue => "Просроченные задачи",
            TaskFilter.Today => "Задачи на сегодня",
            TaskFilter.Week => "Задачи на ближайшие 7 дней (включая просроченные)",
            _ => "Открытые задачи",
        };

        if (tasks.Count == 0)
        {
            return $"{title}: нет.";
        }

        var sb = new StringBuilder($"{title} ({tasks.Count}), сегодня {Date(clock.Today)}:").AppendLine();
        foreach (var task in tasks)
        {
            sb.Append("- ").AppendLine(TaskLine(task));
        }

        return sb.ToString();
    }

    public string TaskCreated(TaskItem task) => $"Создана задача: {TaskLine(task)}";

    public string TaskUpdated(TaskUpdateResult result) =>
        $"Задача #{result.Task.Id} «{result.Task.Title}» изменена: {string.Join("; ", result.Changes)}.{Environment.NewLine}Сейчас: {TaskLine(result.Task)}";

    public string TaskCompleted(TaskItem task, bool resultSaved)
    {
        var linked = task.ContactId is not null || task.DealId is not null;
        return $"Задача #{task.Id} «{task.Title}» выполнена." + (resultSaved && linked
            ? " Результат записан в задачу и в историю."
            : resultSaved ? " Результат записан в задачу." : "");
    }

    // ---------- Строки ----------

    private static string ContactLine(ContactListItem c) => Join(
        $"#{c.Id} {c.FullName}",
        c.Position,
        c.CompanyName is null ? null : $"{c.CompanyName} (компания #{c.CompanyId})",
        c.Email,
        c.Phone,
        $"ответственный: {c.OwnerName}");

    private static string DealLine(DealListItem d) => Join(
        $"#{d.Id} «{d.Title}»",
        Money(d.Amount),
        Stage(d.Stage),
        d.CompanyName,
        d.ContactName is null ? null : $"контакт {d.ContactName} (#{d.ContactId})",
        d.ExpectedCloseDate is { } date ? $"закрытие до {Date(date)}" : null,
        $"ответственный: {d.OwnerName}");

    private string ActivityLine(ActivityItem a, bool includeDeal = true) => Join(
        DateTime(a.OccurredAt),
        a.Type.DisplayName(),
        a.Subject + (a.Details is null ? "" : $" — {a.Details}"),
        includeDeal && a.DealTitle is not null ? $"сделка #{a.DealId} «{a.DealTitle}»" : null,
        a.AuthorName);

    private string TaskLine(TaskItem t)
    {
        var due = $"срок {Date(t.DueDate)}" + (!t.IsCompleted && t.DueDate < clock.Today ? " (ПРОСРОЧЕНА)" : "");
        return Join(
            $"#{t.Id} {t.Title}",
            due,
            t.ContactName is null ? null : $"контакт {t.ContactName} (#{t.ContactId})",
            t.DealTitle is null ? null : $"сделка «{t.DealTitle}» (#{t.DealId})",
            t.IsCompleted ? "выполнена" : null,
            t.AssigneeName);
    }

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static void AppendField(StringBuilder sb, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.AppendLine($"- {name}: {value}");
        }
    }

    private static void AppendLines(StringBuilder sb, IEnumerable<string> lines, string empty)
    {
        var any = false;
        foreach (var line in lines)
        {
            sb.Append("- ").AppendLine(line);
            any = true;
        }

        if (!any)
        {
            sb.AppendLine(empty);
        }
    }
}
