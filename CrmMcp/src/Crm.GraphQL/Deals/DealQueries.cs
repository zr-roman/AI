using Crm.Core.Data;
using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Filtering;
using Crm.GraphQL.Infrastructure;
using GreenDonut;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Sorting;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace Crm.GraphQL.Deals;

[QueryType]
public static partial class DealQueries
{
    [GraphQLDescription(
        "Сделки с фильтрацией, сортировкой и курсорной пагинацией: например, " +
        "deals(where: { stage: { in: [PROPOSAL, NEGOTIATION] } }, order: [{ amount: DESC }]). " +
        "В отличие от REST, по умолчанию возвращаются все сделки, включая закрытые. Без order — сначала крупные.")]
    [UsePaging]
    [UseFiltering(typeof(DealFilterInputType))]
    [UseSorting(typeof(DealSortInputType))]
    public static IQueryable<Deal> GetDeals(CrmDbContext db, ISortingContext sorting)
    {
        sorting.UseDefaultOrder<Deal, int>(q => q.OrderByDescending(d => d.Amount), d => d.Id);
        return db.Deals.AsNoTracking();
    }

    [GraphQLDescription("Сделка по ID.")]
    public static async Task<Deal?> GetDealAsync(
        int id,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken)
        => await dealById.LoadAsync(id, cancellationToken);

    [GraphQLDescription("Сводка по воронке: открытые сделки по стадиям, взвешенный прогноз, выигранное за месяц, зависшие.")]
    public static async Task<PipelineSummary> GetPipelineAsync(
        DealService deals,
        CancellationToken cancellationToken,
        [GraphQLDescription("true — только сделки текущего пользователя")] bool onlyMine = false)
        => await deals.GetPipelineSummaryAsync(onlyMine, cancellationToken);

    [GraphQLDescription(
        "Открытые сделки без движения дольше inactiveDays дней: стадия не менялась и в истории нет записей. " +
        "Сначала самые давно забытые.")]
    public static async Task<StaleDealList> GetStaleDealsAsync(
        DealService deals,
        CancellationToken cancellationToken,
        [GraphQLDescription("Порог в днях, от 7 до 365. Без него — порог из сводки по воронке")] int? inactiveDays = null,
        [GraphQLDescription("true — только сделки текущего пользователя")] bool onlyMine = false,
        [GraphQLDescription("Ответственный: имя, фамилия или email. Не передавай вместе с onlyMine")] string? owner = null)
        => await deals.ListStaleAsync(inactiveDays, onlyMine, owner, cancellationToken);

    [GraphQLDescription("Справочник стадий воронки: название, описание и вероятность закрытия.")]
    public static IReadOnlyList<DealStageInfo> GetDealStages()
        => DealStageInfo.All;
}

/// <summary>Стадия воронки для справочника. Тот же текст, что в MCP-ресурсе crm://reference/deal-stages.</summary>
public sealed record DealStageInfo(DealStage Stage, string DisplayName, string Description, decimal Probability, bool IsClosed)
{
    public static IReadOnlyList<DealStageInfo> All { get; } = Enum.GetValues<DealStage>()
        .Select(s => new DealStageInfo(s, s.DisplayName(), s.Description(), s.Probability(), s.IsClosed()))
        .ToArray();
}

// DTO из сервиса содержат плоскую «карточку» сделки (DealListItem). В GraphQL вместо неё отдаём саму сделку:
// клиент выбирает нужные поля и связи, а сущности догружаются одним запросом через DataLoader.

[ObjectType<StaleDealItem>]
public static partial class StaleDealItemNode
{
    public static async Task<Deal> GetDealAsync(
        [Parent] StaleDealItem item,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken)
        => await dealById.LoadRequiredAsync(item.Deal.Id, cancellationToken);
}

[ObjectType<StaleDealsPreview>]
public static partial class StaleDealsPreviewNode
{
    public static async Task<IReadOnlyList<Deal>> GetDealsAsync(
        [Parent] StaleDealsPreview preview,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken)
        => await dealById.LoadRequiredAsync(preview.Deals.Select(d => d.Id).ToArray(), cancellationToken);
}
