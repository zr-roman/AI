namespace Crm.Core.Services;

/// <summary>
/// От чьего имени выполняется операция. Агент всегда работает с правами конкретного пользователя:
/// в HTTP-хосте — того, чей API-ключ пришёл в запросе, в stdio-хосте — пользователя из конфигурации.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Id пользователя. Бросает <see cref="CrmForbiddenException"/>, если пользователь не аутентифицирован.</summary>
    int Id { get; }

    string FullName { get; }

    bool IsAdmin { get; }
}

internal static class CurrentUserExtensions
{
    /// <summary>Менять запись может её владелец или администратор.</summary>
    public static void EnsureCanModify(this ICurrentUser user, int ownerId, string what)
    {
        if (!user.IsAdmin && user.Id != ownerId)
        {
            throw new CrmForbiddenException(
                $"Нет прав: {what} закреплена за другим менеджером. Изменить её может ответственный или администратор.");
        }
    }

    public static void EnsureAdmin(this ICurrentUser user, string operation)
    {
        if (!user.IsAdmin)
        {
            throw new CrmForbiddenException($"Операция «{operation}» доступна только администратору.");
        }
    }
}
