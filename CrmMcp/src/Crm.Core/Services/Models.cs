using Crm.Core.Domain;

namespace Crm.Core.Services;

// Модели чтения (то, что отдают сервисы) и команды (то, что принимают).
// Их используют и REST-эндпоинты, и MCP-инструменты.

public sealed record ContactListItem(
    int Id,
    string FullName,
    string? Position,
    int? CompanyId,
    string? CompanyName,
    string? Email,
    string? Phone,
    string OwnerName,
    DateTimeOffset CreatedAt);

public sealed record ContactCard(
    ContactListItem Contact,
    IReadOnlyList<DealListItem> OpenDeals,
    IReadOnlyList<ActivityItem> RecentActivities,
    IReadOnlyList<TaskItem> OpenTasks);

public sealed record NewContact(
    string FirstName,
    string LastName,
    string? Email = null,
    string? Phone = null,
    string? Position = null,
    string? CompanyName = null);

public sealed record ContactCreated(ContactListItem Contact, bool CompanyCreated);

public sealed record DealListItem(
    int Id,
    string Title,
    decimal Amount,
    DealStage Stage,
    int? CompanyId,
    string? CompanyName,
    int? ContactId,
    string? ContactName,
    string OwnerName,
    DateOnly? ExpectedCloseDate,
    DateTimeOffset StageChangedAt);

public sealed record DealCard(
    DealListItem Deal,
    string? LostReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ActivityItem> Activities,
    IReadOnlyList<TaskItem> OpenTasks);

public sealed record NewDeal(
    string Title,
    decimal Amount,
    int? ContactId = null,
    int? CompanyId = null,
    DateOnly? ExpectedCloseDate = null,
    DealStage Stage = DealStage.Lead);

/// <summary>Фильтр списка сделок. Без стадии возвращаются только открытые сделки.</summary>
public sealed record DealFilter(DealStage? Stage = null, bool OnlyMine = false, string? Query = null);

public sealed record MoveDealStage(DealStage Stage, string? LostReason = null);

/// <summary>Изменение сделки. null — поле не меняется; чтобы убрать дату закрытия, передай ClearExpectedCloseDate.</summary>
public sealed record DealUpdate(
    string? Title = null,
    decimal? Amount = null,
    DateOnly? ExpectedCloseDate = null,
    bool ClearExpectedCloseDate = false);

/// <summary>Сделка после изменения и человекочитаемый список изменений («сумма: 1 → 2 ₽»).</summary>
public sealed record DealUpdateResult(DealListItem Deal, IReadOnlyList<string> Changes);

public sealed record StageChangeResult(DealListItem Deal, DealStage PreviousStage);

public sealed record PipelineStage(DealStage Stage, int Count, decimal Amount, decimal WeightedAmount);

public sealed record PipelineSummary(
    IReadOnlyList<PipelineStage> Stages,
    int OpenCount,
    decimal OpenAmount,
    decimal WeightedForecast,
    int WonThisMonthCount,
    decimal WonThisMonthAmount,
    int StaleCount,
    int StaleDays);

public sealed record StaleDealsPreview(int InactiveDays, IReadOnlyList<DealListItem> Deals, string ConfirmationToken);

public sealed record ActivityItem(
    int Id,
    ActivityType Type,
    string Subject,
    string? Details,
    DateTimeOffset OccurredAt,
    string AuthorName,
    int? ContactId,
    string? ContactName,
    int? DealId,
    string? DealTitle);

public sealed record NewActivity(
    ActivityType Type,
    string Subject,
    string? Details = null,
    int? ContactId = null,
    int? DealId = null,
    DateTimeOffset? OccurredAt = null);

public sealed record TaskItem(
    int Id,
    string Title,
    string? Details,
    DateOnly DueDate,
    bool IsCompleted,
    string AssigneeName,
    int? ContactId,
    string? ContactName,
    int? DealId,
    string? DealTitle);

public sealed record NewTask(
    string Title,
    DateOnly DueDate,
    string? Details = null,
    int? ContactId = null,
    int? DealId = null);

public sealed record CompleteTask(string? ResultNote = null);

public enum TaskFilter
{
    /// <summary>Все невыполненные.</summary>
    Open,

    /// <summary>Просроченные.</summary>
    Overdue,

    /// <summary>Со сроком сегодня.</summary>
    Today,

    /// <summary>Со сроком в ближайшие 7 дней, включая просроченные.</summary>
    Week,
}
