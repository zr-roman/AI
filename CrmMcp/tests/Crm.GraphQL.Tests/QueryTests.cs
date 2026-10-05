using System.Net;
using System.Text.Json.Nodes;
using Crm.GraphQL.Tests.Infrastructure;

namespace Crm.GraphQL.Tests;

/// <summary>Запросы на чтение. Данные — демо-набор из CrmSeeder, тесты этого класса их не меняют.</summary>
public sealed class QueryTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact]
    public async Task Request_without_api_key_is_rejected()
    {
        using var client = api.CreateClient();

        var response = await client.PostGraphQLAsync("{ me { fullName } }");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_is_the_user_whose_api_key_was_sent()
    {
        using var client = api.CreateClient(CrmApiFactory.BorisKey);

        var response = await client.PostGraphQLAsync("{ me { fullName email role } }");

        var me = response.EnsureNoErrors().Data!["me"]!;
        Assert.Equal("Борис Козлов", me["fullName"]!.GetValue<string>());
        Assert.Equal("boris@crm.local", me["email"]!.GetValue<string>());
        Assert.Equal("MANAGER", me["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Deals_are_filtered_sorted_and_paged_by_cursor()
    {
        // В демо-данных на стадиях PROPOSAL и NEGOTIATION три сделки: 3,6 млн, 1,25 млн и 480 тыс.
        const string query = """
            query Deals($after: String) {
              deals(first: 2, after: $after, where: { stage: { in: [PROPOSAL, NEGOTIATION] } }, order: [{ amount: DESC }]) {
                totalCount
                nodes { title amount stage }
                pageInfo { hasNextPage endCursor }
              }
            }
            """;
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);

        var firstPage = (await client.PostGraphQLAsync(query)).EnsureNoErrors().Data!["deals"]!;

        Assert.Equal(3, firstPage["totalCount"]!.GetValue<int>());
        Assert.Equal(["Линия упаковки", "Складские стеллажи для терминала в Подольске"], Titles(firstPage));
        Assert.True(firstPage["pageInfo"]!["hasNextPage"]!.GetValue<bool>());

        var after = firstPage["pageInfo"]!["endCursor"]!.GetValue<string>();
        var secondPage = (await client.PostGraphQLAsync(query, new { after })).EnsureNoErrors().Data!["deals"]!;

        Assert.Equal(["Годовое сервисное обслуживание"], Titles(secondPage));
        Assert.False(secondPage["pageInfo"]!["hasNextPage"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Deals_can_be_filtered_by_related_entities()
    {
        // Фильтр по связанным сущностям транслируется в SQL через JOIN, а не фильтруется в памяти
        const string query = """
            {
              deals(where: { company: { industry: { eq: "Логистика" } }, owner: { email: { startsWith: "anna" } } }, order: [{ amount: DESC }]) {
                nodes { title company { name } owner { fullName } }
              }
            }
            """;
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);

        var deals = (await client.PostGraphQLAsync(query)).EnsureNoErrors().Data!["deals"]!;

        Assert.Equal(
            ["Складские стеллажи для терминала в Подольске", "Годовое сервисное обслуживание", "Пилотная поставка стеллажей"],
            Titles(deals));
        Assert.All(deals["nodes"]!.AsArray(), deal =>
        {
            Assert.Equal("ООО «Вектор Логистик»", deal!["company"]!["name"]!.GetValue<string>());
            Assert.Equal("Анна Смирнова", deal["owner"]!["fullName"]!.GetValue<string>());
        });
    }

    [Fact]
    public async Task Related_entities_are_batched_by_DataLoader_instead_of_N_plus_1()
    {
        // 8 сделок, у каждой ответственный, контакт с компанией и компания. Без DataLoader это 1 + 8 × 3 = 25 запросов
        // (и ещё по одному на компанию каждого контакта). С DataLoader — по одному запросу на тип связи.
        const string query = """
            {
              deals(first: 20) {
                nodes {
                  title
                  owner { fullName }
                  contact { fullName company { name } }
                  company { name }
                }
              }
            }
            """;
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);
        api.SqlCommands.Reset();

        var deals = (await client.PostGraphQLAsync(query)).EnsureNoErrors().Data!["deals"]!["nodes"]!.AsArray();

        Assert.Equal(8, deals.Count);
        Assert.All(deals, deal => Assert.NotNull(deal!["owner"]));

        // 1 — проверка API-ключа, 1 — страница сделок, по 1 на пользователей, контакты и компании
        Assert.Equal(5, api.SqlCommands.Count);
    }

    [Fact]
    public async Task Search_contacts_ignores_case()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);

        var response = await client.PostGraphQLAsync("""{ searchContacts(query: "ПЕТРОВ") { fullName company { name } } }""");

        var contact = Assert.Single(response.EnsureNoErrors().Data!["searchContacts"]!.AsArray());
        Assert.Equal("Иван Петров", contact!["fullName"]!.GetValue<string>());
        Assert.Equal("ООО «Вектор Логистик»", contact["company"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unknown_id_returns_null_without_error()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);

        var response = await client.PostGraphQLAsync("{ deal(id: 100500) { title } }");

        Assert.Null(response.EnsureNoErrors().Data!["deal"]);
    }

    [Fact]
    public async Task Admin_only_field_is_denied_to_manager_while_rest_of_query_executes()
    {
        using var client = api.CreateClient(CrmApiFactory.BorisKey);

        var response = await client.PostGraphQLAsync("{ me { fullName } auditLog(first: 5) { nodes { operation } } }");

        Assert.Equal("Борис Козлов", response.Data!["me"]!["fullName"]!.GetValue<string>());
        Assert.Null(response.Data["auditLog"]);
        var error = Assert.Single(response.Errors);
        Assert.Equal("AUTH_NOT_AUTHORIZED", error!["extensions"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Business_error_in_query_returns_service_message_and_code()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);

        var response = await client.PostGraphQLAsync("{ staleDeals(inactiveDays: 3) { totalCount } }");

        // Текст из CrmValidationException, а не обезличенное «Unexpected Execution Error»
        var error = Assert.Single(response.Errors);
        Assert.Equal("inactiveDays должен быть от 7 до 365.", error!["message"]!.GetValue<string>());
        Assert.Equal("VALIDATION_FAILED", error["extensions"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Too_expensive_query_is_rejected_before_execution()
    {
        // 50 контактов × сделки × история × автор: cost analysis отклоняет запрос до обращения к БД
        const string query = "{ contacts(first: 50) { nodes { deals { activities { author { fullName } } } } } }";
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);
        api.SqlCommands.Reset();

        var response = await client.PostGraphQLAsync(query);

        Assert.Null(response.Data);
        Assert.NotEmpty(response.Errors);
        Assert.Equal(1, api.SqlCommands.Count); // только проверка API-ключа
    }

    private static string[] Titles(JsonNode connection) =>
        connection["nodes"]!.AsArray().Select(deal => deal!["title"]!.GetValue<string>()).ToArray();
}
