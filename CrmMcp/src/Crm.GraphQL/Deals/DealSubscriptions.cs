using Crm.Core.Domain;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;

namespace Crm.GraphQL.Deals;

// Подписки: мутация публикует событие в топик через ITopicEventSender, сервер рассылает его клиентам,
// подписанным по WebSocket (graphql-ws, /graphql/ws) или SSE (POST /graphql с Accept: text/event-stream).
// Провайдер pub/sub — in-memory: для нескольких экземпляров сервера его меняют на Redis/NATS/Postgres
// без изменений в этом коде.
[SubscriptionType]
public static partial class DealSubscriptions
{
    public const string StageChangedTopic = "DealStageChanged";

    [GraphQLDescription("Смена стадии сделки: перевод по воронке, выигрыш, проигрыш, массовое закрытие зависших.")]
    [Subscribe]
    [Topic(StageChangedTopic)]
    public static DealStageChanged OnDealStageChanged([EventMessage] DealStageChanged message)
        => message;
}

/// <summary>
/// Событие в топике — только идентификаторы и стадии, без сущностей: так его можно передать через внешний брокер,
/// а актуальную сделку каждый подписчик получит через DataLoader в момент доставки.
/// </summary>
public sealed record DealStageChanged(
    int DealId,
    DealStage PreviousStage,
    DealStage Stage,
    int ChangedById,
    DateTimeOffset ChangedAt);

[ObjectType<DealStageChanged>]
public static partial class DealStageChangedNode
{
    public static async Task<Deal> GetDealAsync(
        [Parent] DealStageChanged change,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken)
        => await dealById.LoadRequiredAsync(change.DealId, cancellationToken);

    [GraphQLDescription("Кто перевёл сделку.")]
    public static async Task<User> GetChangedByAsync(
        [Parent] DealStageChanged change,
        IUserByIdDataLoader userById,
        CancellationToken cancellationToken)
        => await userById.LoadRequiredAsync(change.ChangedById, cancellationToken);
}
