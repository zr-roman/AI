using Crm.Core.Data;
using Crm.Core.Domain;
using Crm.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Crm.Mcp.Stdio;

/// <summary>
/// В stdio нет HTTP-аутентификации: процесс запускается локально MCP-клиентом,
/// поэтому пользователь CRM задаётся в конфигурации (Crm:StdioUserEmail).
/// </summary>
public sealed class StdioCurrentUser(IDbContextFactory<CrmDbContext> dbFactory) : ICurrentUser
{
    private UserSnapshot? _user;

    public bool IsAuthenticated => _user is not null;

    public int Id => User.Id;

    public string FullName => User.FullName;

    public bool IsAdmin => User.IsAdmin;

    private UserSnapshot User => _user ?? throw new CrmForbiddenException("Пользователь stdio-сервера не определён.");

    public async Task SignInAsync(string? email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                "Не задан пользователь: укажи Crm:StdioUserEmail в appsettings.json или в переменной окружения Crm__StdioUserEmail.");
        }

        var normalized = email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Email.ToLower() == normalized)
            .Select(u => new UserSnapshot(u.Id, u.FullName, u.Role == UserRole.Admin))
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Пользователь {email} не найден в CRM.");

        _user = user;
    }

    private sealed record UserSnapshot(int Id, string FullName, bool IsAdmin);
}
