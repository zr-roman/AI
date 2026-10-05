using Crm.Core.Domain;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;

namespace Crm.GraphQL.Activities;

[ObjectType<Activity>]
public static partial class ActivityNode
{
    static partial void Configure(IObjectTypeDescriptor<Activity> descriptor)
    {
        descriptor.Description("Запись в истории взаимодействий: звонок, письмо, встреча, заметка или смена стадии.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(a => a.Id);
        descriptor.Field(a => a.Type);
        descriptor.Field(a => a.Subject);
        descriptor.Field(a => a.Details);
        descriptor.Field(a => a.OccurredAt);
    }

    public static async Task<Contact?> GetContactAsync(
        [Parent] Activity activity,
        IContactByIdDataLoader contactById,
        CancellationToken cancellationToken)
        => activity.ContactId is { } contactId
            ? await contactById.LoadAsync(contactId, cancellationToken)
            : null;

    public static async Task<Deal?> GetDealAsync(
        [Parent] Activity activity,
        IDealByIdDataLoader dealById,
        CancellationToken cancellationToken)
        => activity.DealId is { } dealId
            ? await dealById.LoadAsync(dealId, cancellationToken)
            : null;

    [GraphQLDescription("Кто сделал запись.")]
    public static async Task<User> GetAuthorAsync(
        [Parent] Activity activity,
        IUserByIdDataLoader userById,
        CancellationToken cancellationToken)
        => await userById.LoadRequiredAsync(activity.AuthorId, cancellationToken);
}
