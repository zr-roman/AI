using System.Globalization;
using System.Security.Claims;
using Crm.Core.Domain;
using Crm.Core.Services;

namespace Crm.Api.Auth;

/// <summary>
/// Текущий пользователь из HttpContext. MCP в stateless-режиме обрабатывает каждый вызов
/// внутри обычного HTTP-запроса, поэтому IHttpContextAccessor работает и в инструментах.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int Id =>
        int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new CrmForbiddenException("Требуется аутентификация.");

    public string FullName => Principal?.Identity?.Name ?? "";

    public bool IsAdmin => Principal?.IsInRole(nameof(UserRole.Admin)) == true;
}
