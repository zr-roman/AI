using Crm.GraphQL.Tests.Infrastructure;

namespace Crm.GraphQL.Tests;

/// <summary>
/// Мутации — тонкий адаптер над сервисами Crm.Core. Проверяем, что права, валидация и бизнес-ошибки сервисов
/// доходят до клиента как типизированные ошибки в payload (mutation conventions), а не как исключения.
/// </summary>
public sealed class MutationTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    private const string ErrorsSelection = "errors { __typename ... on Error { message } }";

    [Fact]
    public async Task CreateDeal_takes_owner_from_api_key_and_company_from_contact()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);
        var ivanId = await FindContactIdAsync(client, "Петров");

        var response = await client.PostGraphQLAsync(
            $$"""
            mutation CreateDeal($input: CreateDealInput!) {
              createDeal(input: $input) {
                deal { title amount stage owner { fullName } contact { fullName } company { name } }
                {{ErrorsSelection}}
              }
            }
            """,
            new { input = new { title = "Стеллажи для второго склада", amount = 750_000, contactId = ivanId, stage = "QUALIFIED" } });

        var payload = response.EnsureNoErrors().Data!["createDeal"]!;
        Assert.Null(payload["errors"]);
        var deal = payload["deal"]!;
        Assert.Equal("Стеллажи для второго склада", deal["title"]!.GetValue<string>());
        Assert.Equal(750_000m, deal["amount"]!.GetValue<decimal>());
        Assert.Equal("QUALIFIED", deal["stage"]!.GetValue<string>());
        Assert.Equal("Анна Смирнова", deal["owner"]!["fullName"]!.GetValue<string>());
        Assert.Equal("Иван Петров", deal["contact"]!["fullName"]!.GetValue<string>());
        Assert.Equal("ООО «Вектор Логистик»", deal["company"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Moving_deal_to_lost_without_reason_returns_validation_error_in_payload()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);
        var dealId = await FindDealIdAsync(client, "Годовое сервисное обслуживание");

        var response = await client.PostGraphQLAsync(
            $$"""mutation { moveDealStage(input: { dealId: {{dealId}}, stage: LOST }) { deal { stage } {{ErrorsSelection}} } }""");

        var payload = response.EnsureNoErrors().Data!["moveDealStage"]!;
        Assert.Null(payload["deal"]);
        var error = Assert.Single(payload["errors"]!.AsArray());
        Assert.Equal("ValidationError", error!["__typename"]!.GetValue<string>());
        Assert.Equal("Для перевода в Lost укажи причину проигрыша (lostReason).", error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Moving_deal_stage_is_written_to_deal_history()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);
        var dealId = await FindDealIdAsync(client, "Складские стеллажи для терминала в Подольске");

        var response = await client.PostGraphQLAsync(
            $$"""
            mutation {
              moveDealStage(input: { dealId: {{dealId}}, stage: NEGOTIATION }) {
                previousStage
                deal { stage probability activities { type subject } }
                {{ErrorsSelection}}
              }
            }
            """);

        var payload = response.EnsureNoErrors().Data!["moveDealStage"]!;
        Assert.Null(payload["errors"]);
        Assert.Equal("PROPOSAL", payload["previousStage"]!.GetValue<string>());
        Assert.Equal("NEGOTIATION", payload["deal"]!["stage"]!.GetValue<string>());
        Assert.Equal(0.75m, payload["deal"]!["probability"]!.GetValue<decimal>());

        // История отдаётся сначала новыми записями: смена стадии — первая
        var latest = payload["deal"]!["activities"]!.AsArray()[0]!;
        Assert.Equal("STAGE_CHANGE", latest["type"]!.GetValue<string>());
        Assert.Equal("Стадия: Proposal → Negotiation", latest["subject"]!.GetValue<string>());
    }

    [Fact]
    public async Task Manager_cannot_change_deal_of_another_manager()
    {
        using var annaClient = api.CreateClient(CrmApiFactory.AnnaKey);
        var annasDealId = await FindDealIdAsync(annaClient, "Линия упаковки");
        using var borisClient = api.CreateClient(CrmApiFactory.BorisKey);

        var response = await borisClient.PostGraphQLAsync(
            $$"""mutation { updateDeal(input: { dealId: {{annasDealId}}, amount: 1 }) { deal { amount } {{ErrorsSelection}} } }""");

        var error = Assert.Single(response.EnsureNoErrors().Data!["updateDeal"]!["errors"]!.AsArray());
        Assert.Equal("ForbiddenError", error!["__typename"]!.GetValue<string>());
    }

    [Fact]
    public async Task Changing_unknown_deal_returns_not_found_error()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);

        var response = await client.PostGraphQLAsync(
            $$"""mutation { updateDeal(input: { dealId: 100500, amount: 1 }) { deal { amount } {{ErrorsSelection}} } }""");

        var error = Assert.Single(response.EnsureNoErrors().Data!["updateDeal"]!["errors"]!.AsArray());
        Assert.Equal("NotFoundError", error!["__typename"]!.GetValue<string>());
        Assert.Equal("Сделка #100500 не найдена.", error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Task_can_be_created_and_completed_once()
    {
        using var client = api.CreateClient(CrmApiFactory.BorisKey);
        var tomorrow = CrmToday().AddDays(1).ToString("yyyy-MM-dd");

        var created = await client.PostGraphQLAsync(
            $$"""
            mutation {
              createTask(input: { title: "Отправить счёт", dueDate: "{{tomorrow}}" }) {
                task { id title dueDate isCompleted isOverdue assignee { fullName } }
                {{ErrorsSelection}}
              }
            }
            """);

        var task = created.EnsureNoErrors().Data!["createTask"]!["task"]!;
        Assert.Equal(tomorrow, task["dueDate"]!.GetValue<string>());
        Assert.False(task["isCompleted"]!.GetValue<bool>());
        Assert.False(task["isOverdue"]!.GetValue<bool>());
        Assert.Equal("Борис Козлов", task["assignee"]!["fullName"]!.GetValue<string>());

        var taskId = task["id"]!.GetValue<int>();
        const string complete = """
            mutation Complete($taskId: Int!) {
              completeTask(input: { taskId: $taskId, resultNote: "Счёт отправлен" }) {
                task { isCompleted details }
                errors { __typename }
              }
            }
            """;

        var completed = (await client.PostGraphQLAsync(complete, new { taskId })).EnsureNoErrors().Data!["completeTask"]!;
        Assert.True(completed["task"]!["isCompleted"]!.GetValue<bool>());
        Assert.Equal("Результат: Счёт отправлен", completed["task"]!["details"]!.GetValue<string>());

        var again = (await client.PostGraphQLAsync(complete, new { taskId })).EnsureNoErrors().Data!["completeTask"]!;
        Assert.Equal("ValidationError", again["errors"]!.AsArray()[0]!["__typename"]!.GetValue<string>());
    }

    [Fact]
    public async Task Task_due_date_in_the_past_is_rejected()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);
        var yesterday = CrmToday().AddDays(-1).ToString("yyyy-MM-dd");

        var response = await client.PostGraphQLAsync(
            $$"""mutation { createTask(input: { title: "Вчерашняя задача", dueDate: "{{yesterday}}" }) { task { id } {{ErrorsSelection}} } }""");

        var error = Assert.Single(response.EnsureNoErrors().Data!["createTask"]!["errors"]!.AsArray());
        Assert.Equal("ValidationError", error!["__typename"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stale_deals_are_closed_in_two_steps_and_only_by_admin()
    {
        const string preview = "{ staleDealsClosurePreview(inactiveDays: 30) { confirmationToken deals { title } } }";
        const string close = """
            mutation Close($token: String!) {
              closeStaleDeals(input: { inactiveDays: 30, confirmationToken: $token }) {
                deals { title stage lostReason }
                errors { __typename ... on Error { message } }
              }
            }
            """;

        // Менеджеру поле недоступно: [Authorize(Roles = Admin)] в схеме, ещё до сервиса
        using var borisClient = api.CreateClient(CrmApiFactory.BorisKey);
        var denied = await borisClient.PostGraphQLAsync(preview);
        Assert.Equal("AUTH_NOT_AUTHORIZED", denied.Errors[0]!["extensions"]!["code"]!.GetValue<string>());

        // Шаг 1: превью ничего не меняет и выдаёт токен — хэш набора сделок
        using var annaClient = api.CreateClient(CrmApiFactory.AnnaKey);
        var previewData = (await annaClient.PostGraphQLAsync(preview)).EnsureNoErrors().Data!["staleDealsClosurePreview"]!;
        Assert.Equal(
            ["Касса и учёт для магазина", "Поставка расходных материалов"],
            previewData["deals"]!.AsArray().Select(d => d!["title"]!.GetValue<string>()).Order().ToArray());

        // Чужой токен не подходит: закрыть можно только то, что показали в превью
        var conflict = (await annaClient.PostGraphQLAsync(close, new { token = "000000000000" })).EnsureNoErrors().Data!["closeStaleDeals"]!;
        Assert.Equal("ConflictError", conflict["errors"]!.AsArray()[0]!["__typename"]!.GetValue<string>());

        // Шаг 2: с токеном из превью сделки уходят в LOST с причиной
        var token = previewData["confirmationToken"]!.GetValue<string>();
        var closed = (await annaClient.PostGraphQLAsync(close, new { token })).EnsureNoErrors().Data!["closeStaleDeals"]!;
        Assert.Null(closed["errors"]);
        Assert.All(closed["deals"]!.AsArray(), deal =>
        {
            Assert.Equal("LOST", deal!["stage"]!.GetValue<string>());
            Assert.Contains("массовое закрытие", deal["lostReason"]!.GetValue<string>());
        });
        Assert.Equal(2, closed["deals"]!.AsArray().Count);
    }

    private static async Task<int> FindDealIdAsync(HttpClient client, string title)
    {
        var response = await client.PostGraphQLAsync(
            "query Deal($title: String!) { deals(where: { title: { eq: $title } }) { nodes { id } } }",
            new { title });

        var deal = Assert.Single(response.EnsureNoErrors().Data!["deals"]!["nodes"]!.AsArray());
        return deal!["id"]!.GetValue<int>();
    }

    private static async Task<int> FindContactIdAsync(HttpClient client, string query)
    {
        var response = await client.PostGraphQLAsync(
            "query Search($query: String!) { searchContacts(query: $query) { id } }",
            new { query });

        var contact = Assert.Single(response.EnsureNoErrors().Data!["searchContacts"]!.AsArray());
        return contact!["id"]!.GetValue<int>();
    }

    // «Сегодня» сервисы считают в часовом поясе отдела продаж (Crm:TimeZone)
    private static DateOnly CrmToday() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")).DateTime);
}
