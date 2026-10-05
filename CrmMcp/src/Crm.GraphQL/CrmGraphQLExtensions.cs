using ChilliCream.Nitro.App;
using Crm.Core.Domain;
using Crm.GraphQL.Errors;
using Crm.GraphQL.Filtering;
using HotChocolate.Data;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Crm.GraphQL;

public static class CrmGraphQLExtensions
{
    /// <summary>
    /// Регистрирует GraphQL-сервер CRM на Hot Chocolate. Реализацию ICurrentUser, аутентификацию
    /// и сервисы Crm.Core регистрирует хост — так же, как для MCP-сервера.
    /// </summary>
    public static IRequestExecutorBuilder AddCrmGraphQL(this IServiceCollection services)
    {
        // AddGraphQLServer по умолчанию включает защиту: анализ стоимости запроса (cost analysis),
        // запрет интроспекции вне Development и лимит циклов вида deal → contact → deals → contact
        return services
            .AddGraphQLServer()
            // Сгенерировано source generator: все [QueryType], [MutationType], [ObjectType<T>], [DataLoader]
            .AddCrmTypes()
            // [Authorize] на полях проверяется через ASP.NET Core: роли из claims API-ключа
            .AddAuthorization()
            .AddFiltering(filters => filters
                .AddDefaults()
                .BindRuntimeType<User, UserFilterInputType>()
                .BindRuntimeType<Company, CompanyFilterInputType>()
                .BindRuntimeType<Contact, ContactFilterInputType>()
                .BindRuntimeType<Deal, DealFilterInputType>()
                .BindRuntimeType<AuditEntry, AuditEntryFilterInputType>())
            .AddSorting(sorting => sorting
                .AddDefaults()
                .BindRuntimeType<User, UserSortInputType>()
                .BindRuntimeType<Company, CompanySortInputType>()
                .BindRuntimeType<Contact, ContactSortInputType>()
                .BindRuntimeType<Deal, DealSortInputType>()
                .BindRuntimeType<AuditEntry, AuditEntrySortInputType>())
            // input/payload для каждой мутации и типизированные ошибки в payload ([Error<T>])
            .AddMutationConventions(applyToAllMutations: true)
            .AddInMemorySubscriptions()
            .AddErrorFilter<CrmErrorFilter>()
            .AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)
            .ModifyOptions(options =>
            {
                // Мутации одного запроса выполняются по очереди и по умолчанию делят scoped DbContext.
                // Если мутация упала после изменения отслеживаемой сущности, следующая сохранила бы
                // её незавершённые изменения. Поэтому каждой мутации — свой DI-scope и свой DbContext.
                options.DefaultMutationDependencyInjectionScope = DependencyInjectionScope.Resolver;
            })
            .ModifyPagingOptions(options =>
            {
                options.DefaultPageSize = 20;
                options.MaxPageSize = 50;
                options.IncludeTotalCount = true;
            });
    }

    /// <summary>
    /// GraphQL доступен только с API-ключом, как /api и /mcp: анонимный запрос получит 401 ещё до разбора
    /// документа. IDE Nitro (только в Development) — статическая страница без данных, её можно открыть без ключа.
    /// </summary>
    public static IEndpointRouteBuilder MapCrmGraphQL(this IEndpointRouteBuilder endpoints)
    {
        // Запросы, мутации и подписки через SSE (Accept: text/event-stream)
        endpoints.MapGraphQLHttp("/graphql").RequireAuthorization();

        // Подписки по WebSocket, протокол graphql-transport-ws. Ключ передаётся заголовком при установке соединения,
        // поэтому это путь для серверных клиентов: браузерный WebSocket заголовки не умеет, и Nitro переключится на SSE
        endpoints.MapGraphQLWebSocket("/graphql/ws").RequireAuthorization();

        // Вне Development Hot Chocolate запрещает интроспекцию. Схему в SDL и IDE тоже публикуем только в Development:
        // иначе /graphql/schema.graphql отдавал бы в продакшне ту самую схему, которую закрыла интроспекция
        if (!endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return endpoints;
        }

        // Схема в SDL — для генерации клиентов и проверки обратной совместимости
        endpoints.MapGraphQLSchema("/graphql/schema.graphql").RequireAuthorization();

        // IDE: в документе на вкладке HTTP Headers добавь Authorization: Bearer <API-ключ>
        endpoints.MapNitroApp("/graphql/ui")
            .WithOptions(options =>
            {
                // По умолчанию Nitro проксирует IDE с CDN chillicream.com. Embedded раздаёт её из сборки
                // ChilliCream.Nitro.App: работает без интернета, а API не ходит наружу за чужим контентом
                options.ServeMode = ServeMode.Embedded;

                // Иначе IDE отправляла бы запросы на собственный адрес /graphql/ui
                options.UseBrowserUrlAsGraphQLEndpoint = false;
                options.GraphQLEndpoint = "/graphql";
                options.Title = "CRM GraphQL";
            });

        return endpoints;
    }
}
