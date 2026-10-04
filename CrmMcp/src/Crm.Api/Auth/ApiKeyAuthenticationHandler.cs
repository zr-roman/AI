using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Crm.Core.Data;
using Crm.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crm.Api.Auth;

/// <summary>
/// Аутентификация по API-ключу: заголовок <c>Authorization: Bearer &lt;key&gt;</c> или <c>X-Api-Key: &lt;key&gt;</c>.
/// Ключ сопоставляется с пользователем CRM, и дальше все инструменты работают с его правами.
/// Для публичного сервера замени на OAuth — см. раздел «Продакшн» в README.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CrmDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ReadApiKey();
        if (apiKey is null)
        {
            return AuthenticateResult.NoResult();
        }

        var hash = ApiKeys.Hash(apiKey);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.ApiKeyHash == hash)
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role })
            .FirstOrDefaultAsync(Context.RequestAborted);

        if (user is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
        ];

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"crm\"";
        return Task.CompletedTask;
    }

    private string? ReadApiKey()
    {
        if (Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader) && !string.IsNullOrWhiteSpace(apiKeyHeader))
        {
            return apiKeyHeader.ToString().Trim();
        }

        const string bearerPrefix = "Bearer ";
        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearerPrefix.Length..].Trim()
            : null;
    }
}
