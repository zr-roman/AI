using Crm.Core.Domain;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Crm.GraphQL.Contacts;

[ObjectType<Contact>]
public static partial class ContactNode
{
    static partial void Configure(IObjectTypeDescriptor<Contact> descriptor)
    {
        descriptor.Description("Контактное лицо клиента.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.FirstName);
        descriptor.Field(c => c.LastName);
        descriptor.Field(c => c.Email);
        descriptor.Field(c => c.Phone);
        descriptor.Field(c => c.Position);
        descriptor.Field(c => c.CreatedAt);
    }

    public static string GetFullName([Parent] Contact contact)
        => $"{contact.FirstName} {contact.LastName}";

    public static async Task<Company?> GetCompanyAsync(
        [Parent] Contact contact,
        ICompanyByIdDataLoader companyById,
        CancellationToken cancellationToken)
        => contact.CompanyId is { } companyId
            ? await companyById.LoadAsync(companyId, cancellationToken)
            : null;

    [GraphQLDescription("Ответственный менеджер.")]
    public static async Task<User> GetOwnerAsync(
        [Parent] Contact contact,
        IUserByIdDataLoader userById,
        CancellationToken cancellationToken)
        => await userById.LoadRequiredAsync(contact.OwnerId, cancellationToken);

    [GraphQLDescription("Сделки контакта, сначала крупные. По умолчанию только открытые.")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<Deal[]> GetDealsAsync(
        [Parent] Contact contact,
        IDealsByContactIdDataLoader dealsByContactId,
        CancellationToken cancellationToken,
        [GraphQLDescription("true — вместе с выигранными и проигранными")] bool includeClosed = false)
    {
        var deals = await dealsByContactId.LoadAsync(contact.Id, cancellationToken) ?? [];
        return includeClosed ? deals : deals.Where(d => !d.Stage.IsClosed()).ToArray();
    }

    [GraphQLDescription("История взаимодействий, сначала новые.")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<Activity[]> GetActivitiesAsync(
        [Parent] Contact contact,
        IActivitiesByContactIdDataLoader activitiesByContactId,
        CancellationToken cancellationToken)
        => await activitiesByContactId.LoadAsync(contact.Id, cancellationToken) ?? [];

    [GraphQLDescription("Задачи по контакту, ближайшие сначала. По умолчанию только невыполненные.")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<CrmTask[]> GetTasksAsync(
        [Parent] Contact contact,
        ITasksByContactIdDataLoader tasksByContactId,
        CancellationToken cancellationToken,
        [GraphQLDescription("true — вместе с выполненными")] bool includeCompleted = false)
    {
        var tasks = await tasksByContactId.LoadAsync(contact.Id, cancellationToken) ?? [];
        return includeCompleted ? tasks : tasks.Where(t => !t.IsCompleted).ToArray();
    }
}
