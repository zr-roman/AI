using Crm.Core.Domain;
using HotChocolate.Types;

namespace Crm.GraphQL.Users;

[ObjectType<User>]
public static partial class UserNode
{
    static partial void Configure(IObjectTypeDescriptor<User> descriptor)
    {
        descriptor.Description("Сотрудник отдела продаж — пользователь CRM.");

        // Белый список полей: хэш API-ключа не попадёт в схему, даже если в сущность добавят новые поля
        descriptor.BindFieldsExplicitly();
        descriptor.Field(u => u.Id);
        descriptor.Field(u => u.FullName);
        descriptor.Field(u => u.Email);
        descriptor.Field(u => u.Role);
    }
}
