using Crm.Core.Data;
using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace Crm.GraphQL.Users;

[QueryType]
public static partial class UserQueries
{
    [GraphQLDescription("Текущий пользователь — тот, чей API-ключ пришёл в запросе.")]
    public static async Task<User> GetMeAsync(
        ICurrentUser currentUser,
        IUserByIdDataLoader userById,
        CancellationToken cancellationToken)
        => await userById.LoadRequiredAsync(currentUser.Id, cancellationToken);

    [GraphQLDescription("Все пользователи CRM: из них выбирают ответственного в фильтрах.")]
    public static async Task<List<User>> GetUsersAsync(
        CrmDbContext db,
        CancellationToken cancellationToken)
        => await db.Users.AsNoTracking()
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);
}
