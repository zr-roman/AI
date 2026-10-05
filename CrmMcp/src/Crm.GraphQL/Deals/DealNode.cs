using Crm.Core.Domain;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Crm.GraphQL.Deals;

[ObjectType<Deal>]
public static partial class DealNode
{
    static partial void Configure(IObjectTypeDescriptor<Deal> descriptor)
    {
        descriptor.Description("Сделка в воронке продаж.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(d => d.Id);
        descriptor.Field(d => d.Title);
        descriptor.Field(d => d.Amount).Description("Сумма в рублях.");
        descriptor.Field(d => d.Stage);
        descriptor.Field(d => d.ExpectedCloseDate).Description("Ожидаемая дата закрытия.");
        descriptor.Field(d => d.LostReason).Description("Причина проигрыша, заполняется для стадии LOST.");
        descriptor.Field(d => d.CreatedAt);
        descriptor.Field(d => d.StageChangedAt);
        descriptor.Field(d => d.ClosedAt);
    }

    [GraphQLDescription("Вероятность закрытия по стадии воронки, от 0 до 1.")]
    public static decimal GetProbability([Parent] Deal deal)
        => deal.Stage.Probability();

    [GraphQLDescription("Взвешенная сумма: сумма × вероятность стадии. Из таких сумм складывается прогноз.")]
    public static decimal GetWeightedAmount([Parent] Deal deal)
        => deal.Amount * deal.Stage.Probability();

    [GraphQLDescription("Сделка закрыта: WON или LOST.")]
    public static bool GetIsClosed([Parent] Deal deal)
        => deal.Stage.IsClosed();

    public static async Task<Contact?> GetContactAsync(
        [Parent] Deal deal,
        IContactByIdDataLoader contactById,
        CancellationToken cancellationToken)
        => deal.ContactId is { } contactId
            ? await contactById.LoadAsync(contactId, cancellationToken)
            : null;

    public static async Task<Company?> GetCompanyAsync(
        [Parent] Deal deal,
        ICompanyByIdDataLoader companyById,
        CancellationToken cancellationToken)
        => deal.CompanyId is { } companyId
            ? await companyById.LoadAsync(companyId, cancellationToken)
            : null;

    [GraphQLDescription("Ответственный менеджер.")]
    public static async Task<User> GetOwnerAsync(
        [Parent] Deal deal,
        IUserByIdDataLoader userById,
        CancellationToken cancellationToken)
        => await userById.LoadRequiredAsync(deal.OwnerId, cancellationToken);

    [GraphQLDescription("История сделки, сначала новые записи: звонки, письма, смены стадий.")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<Activity[]> GetActivitiesAsync(
        [Parent] Deal deal,
        IActivitiesByDealIdDataLoader activitiesByDealId,
        CancellationToken cancellationToken)
        => await activitiesByDealId.LoadAsync(deal.Id, cancellationToken) ?? [];

    [GraphQLDescription("Задачи по сделке, ближайшие сначала. По умолчанию только невыполненные.")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<CrmTask[]> GetTasksAsync(
        [Parent] Deal deal,
        ITasksByDealIdDataLoader tasksByDealId,
        CancellationToken cancellationToken,
        [GraphQLDescription("true — вместе с выполненными")] bool includeCompleted = false)
    {
        var tasks = await tasksByDealId.LoadAsync(deal.Id, cancellationToken) ?? [];
        return includeCompleted ? tasks : tasks.Where(t => !t.IsCompleted).ToArray();
    }
}
