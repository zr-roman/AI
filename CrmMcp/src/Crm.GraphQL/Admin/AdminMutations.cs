using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Deals;
using Crm.GraphQL.Errors;
using GreenDonut;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Subscriptions;
using HotChocolate.Types;

namespace Crm.GraphQL.Admin;

[MutationType]
public static partial class AdminMutations
{
    [Authorize(Roles = [nameof(UserRole.Admin)])]
    [GraphQLDescription(
        "Шаг 2 массового закрытия: переводит зависшие сделки в LOST. Токен — из staleDealsClosurePreview; " +
        "если набор сделок с тех пор изменился, вернётся ConflictError и ничего не изменится. Только для администратора.")]
    [Error<ValidationError>]
    [Error<ConflictError>]
    [Error<ForbiddenError>]
    public static async Task<CloseStaleDealsPayload> CloseStaleDealsAsync(
        [GraphQLDescription("Тот же порог в днях, что в превью")] int inactiveDays,
        [GraphQLDescription("Токен из staleDealsClosurePreview")] string confirmationToken,
        DealService deals,
        IDealByIdDataLoader dealById,
        ICurrentUser currentUser,
        ITopicEventSender eventSender,
        CancellationToken cancellationToken)
    {
        // Стадии до закрытия нужны для событий подписки. Набор сделок тот же, по которому сервис проверит токен
        var preview = await deals.PreviewStaleDealsAsync(inactiveDays, cancellationToken);
        var previousStages = preview.Deals.ToDictionary(d => d.Id, d => d.Stage);

        var closed = await deals.CloseStaleDealsAsync(inactiveDays, confirmationToken, cancellationToken);

        // Закрытые сделки — одним запросом; старые версии из кэша DataLoader сначала убираем
        var ids = closed.Select(d => d.Id).ToArray();
        foreach (var id in ids)
        {
            dealById.RemoveCacheEntry(id);
        }

        var closedDeals = await dealById.LoadRequiredAsync(ids, cancellationToken);

        foreach (var deal in closedDeals)
        {
            if (previousStages.TryGetValue(deal.Id, out var previousStage))
            {
                await eventSender.SendAsync(
                    DealSubscriptions.StageChangedTopic,
                    new DealStageChanged(deal.Id, previousStage, deal.Stage, currentUser.Id, deal.StageChangedAt),
                    cancellationToken);
            }
        }

        return new CloseStaleDealsPayload(closedDeals);
    }
}

public sealed record CloseStaleDealsPayload(IReadOnlyList<Deal> Deals);
