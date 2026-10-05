using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Errors;
using GreenDonut;
using HotChocolate;
using HotChocolate.Subscriptions;
using HotChocolate.Types;

namespace Crm.GraphQL.Deals;

// Mutation conventions: аргументы метода становятся полями input-типа (CreateDealInput), результат — payload.
// [Error<T>] добавляет в payload поле errors с union-типом ошибок, которые может вернуть именно эта мутация.
[MutationType]
public static partial class DealMutations
{
    [GraphQLDescription("Создаёт сделку от имени текущего пользователя. Если указан контакт, компания подставится из его карточки.")]
    [Error<ValidationError>]
    [Error<NotFoundError>]
    public static async Task<CreateDealPayload> CreateDealAsync(
        string title,
        [GraphQLDescription("Сумма в рублях")] decimal amount,
        DealService deals,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken,
        int? contactId = null,
        [GraphQLDescription("ID компании, если контакт не указан или работает в другой компании")] int? companyId = null,
        DateOnly? expectedCloseDate = null,
        // Не `stage = OpenDealStage.Lead`: Hot Chocolate 16.6.7 падает на старте схемы (NullReferenceException
        // в EnumType.ValueToLiteral), когда mutation conventions переносят enum-аргумент со значением по умолчанию
        // в input-тип. Поэтому поле необязательное, а LEAD подставляем сами.
        [GraphQLDescription("Начальная стадия: только открытые, без WON и LOST. По умолчанию LEAD")] OpenDealStage? stage = null)
    {
        var created = await deals.CreateAsync(
            new NewDeal(title, amount, contactId, companyId, expectedCloseDate, (stage ?? OpenDealStage.Lead).ToDealStage()),
            cancellationToken);

        return new CreateDealPayload(await dealById.LoadRequiredAsync(created.Id, cancellationToken));
    }

    [GraphQLDescription(
        "Меняет название, сумму и/или ожидаемую дату закрытия открытой сделки — передавай только то, что меняется. " +
        "Стадию меняет moveDealStage. Изменения записываются в историю сделки.")]
    [Error<ValidationError>]
    [Error<NotFoundError>]
    [Error<ForbiddenError>]
    public static async Task<UpdateDealPayload> UpdateDealAsync(
        int dealId,
        DealService deals,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken,
        string? title = null,
        [GraphQLDescription("Новая сумма в рублях")] decimal? amount = null,
        DateOnly? expectedCloseDate = null,
        [GraphQLDescription("true — убрать ожидаемую дату закрытия")] bool clearExpectedCloseDate = false)
    {
        var result = await deals.UpdateAsync(
            dealId,
            new DealUpdate(title, amount, expectedCloseDate, clearExpectedCloseDate),
            cancellationToken);

        return new UpdateDealPayload(await dealById.ReloadAsync(dealId, cancellationToken), result.Changes);
    }

    [GraphQLDescription(
        "Переводит сделку на другую стадию воронки и записывает смену в историю. Для LOST обязательна причина. " +
        "Подписчики onDealStageChanged получают событие.")]
    [Error<ValidationError>]
    [Error<NotFoundError>]
    [Error<ForbiddenError>]
    public static async Task<MoveDealStagePayload> MoveDealStageAsync(
        int dealId,
        DealStage stage,
        DealService deals,
        IDealByIdDataLoader dealById,
        ICurrentUser currentUser,
        ITopicEventSender eventSender,
        CancellationToken cancellationToken,
        [GraphQLDescription("Причина проигрыша — обязательна для LOST")] string? lostReason = null)
    {
        var result = await deals.MoveStageAsync(dealId, stage, lostReason, cancellationToken);
        var deal = await dealById.ReloadAsync(dealId, cancellationToken);

        await eventSender.SendAsync(
            DealSubscriptions.StageChangedTopic,
            new DealStageChanged(deal.Id, result.PreviousStage, deal.Stage, currentUser.Id, deal.StageChangedAt),
            cancellationToken);

        return new MoveDealStagePayload(deal, result.PreviousStage);
    }
}

public sealed record CreateDealPayload(Deal Deal);

public sealed record UpdateDealPayload(Deal Deal, IReadOnlyList<string> Changes);

public sealed record MoveDealStagePayload(Deal Deal, DealStage PreviousStage);

/// <summary>
/// Стадии, в которых можно создать сделку. Отдельный enum, чтобы схема не предлагала WON и LOST
/// (так же сделано в MCP-инструменте create_deal).
/// </summary>
public enum OpenDealStage
{
    Lead,
    Qualified,
    Proposal,
    Negotiation,
}

internal static class OpenDealStageExtensions
{
    public static DealStage ToDealStage(this OpenDealStage stage) => stage switch
    {
        OpenDealStage.Lead => DealStage.Lead,
        OpenDealStage.Qualified => DealStage.Qualified,
        OpenDealStage.Proposal => DealStage.Proposal,
        OpenDealStage.Negotiation => DealStage.Negotiation,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Неизвестная стадия."),
    };
}
