using System.Text.Json.Serialization;
using Crm.Api;
using Crm.Api.Auth;
using Crm.Api.Endpoints;
using Crm.Core;
using Crm.Core.Data;
using Crm.Core.Services;
using Crm.GraphQL;
using Crm.Mcp;
using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCrmCore(builder.Configuration);

// От чьего имени работаем: пользователь, чей API-ключ пришёл в текущем запросе
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ICurrentUser, HttpCurrentUser>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddAuthorization();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CrmExceptionHandler>();

builder.Services
    .AddCrmMcpServer(auditChannel: "mcp-http")
    .WithHttpTransport(options =>
    {
        // Без сессий: каждый POST обрабатывается независимо, сервер можно масштабировать
        // за балансировщиком. Это режим по умолчанию в SDK 2.x и родной для ревизии MCP 2026-07-28.
        options.SessionMode = HttpServerSessionMode.Stateless;
    })
    // Включает [Authorize] на инструментах: недоступные инструменты пропадают из tools/list
    .AddAuthorizationFilters();

// Третий адаптер над теми же сервисами: GraphQL на Hot Chocolate (src/Crm.GraphQL)
builder.Services.AddCrmGraphQL();

var app = builder.Build();

await app.Services.InitializeCrmDatabaseAsync();

app.UseExceptionHandler();
// Транспорт для GraphQL-подписок по WebSocket
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "CRM MCP server. MCP: POST /mcp, REST: /api/*, GraphQL: /graphql (IDE: /graphql/ui)");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapCrmApi();
app.MapMcp("/mcp").RequireAuthorization();
app.MapCrmGraphQL();

app.Run();
