using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;

namespace Crm.GraphQL.Tasks;

[ObjectType<CrmTask>]
public static partial class TaskNode
{
    static partial void Configure(IObjectTypeDescriptor<CrmTask> descriptor)
    {
        // В C# сущность называется CrmTask, чтобы не путаться с System.Threading.Tasks.Task. В схеме это просто Task
        descriptor.Name("Task");
        descriptor.Description("Задача менеджера: следующий шаг по клиенту или сделке.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(t => t.Id);
        descriptor.Field(t => t.Title);
        descriptor.Field(t => t.Details);
        descriptor.Field(t => t.DueDate);
        descriptor.Field(t => t.IsCompleted);
        descriptor.Field(t => t.CompletedAt);
        descriptor.Field(t => t.CreatedAt);
    }

    [GraphQLDescription("Просрочена: не выполнена, а срок уже прошёл. «Сегодня» считается в часовом поясе отдела продаж.")]
    public static bool GetIsOverdue([Parent] CrmTask task, CrmClock clock)
        => !task.IsCompleted && task.DueDate < clock.Today;

    [GraphQLDescription("Исполнитель.")]
    public static async Task<User> GetAssigneeAsync(
        [Parent] CrmTask task,
        IUserByIdDataLoader userById,
        CancellationToken cancellationToken)
        => await userById.LoadRequiredAsync(task.AssigneeId, cancellationToken);

    public static async Task<Contact?> GetContactAsync(
        [Parent] CrmTask task,
        IContactByIdDataLoader contactById,
        CancellationToken cancellationToken)
        => task.ContactId is { } contactId
            ? await contactById.LoadAsync(contactId, cancellationToken)
            : null;

    public static async Task<Deal?> GetDealAsync(
        [Parent] CrmTask task,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken)
        => task.DealId is { } dealId
            ? await dealById.LoadAsync(dealId, cancellationToken)
            : null;
}
