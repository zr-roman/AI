using Crm.Core.Domain;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;

namespace Crm.GraphQL.Admin;

[ObjectType<AuditEntry>]
public static partial class AuditEntryNode
{
    static partial void Configure(IObjectTypeDescriptor<AuditEntry> descriptor)
        => descriptor.Description("Запись аудита: кто, через какой канал и с какими аргументами вызвал операцию.");

    [GraphQLDescription("Пользователь, от имени которого выполнялась операция.")]
    public static async Task<User?> GetUserAsync(
        [Parent] AuditEntry entry,
        IUserByIdDataLoader userById,
        CancellationToken cancellationToken)
        => entry.UserId is { } userId
            ? await userById.LoadAsync(userId, cancellationToken)
            : null;
}
