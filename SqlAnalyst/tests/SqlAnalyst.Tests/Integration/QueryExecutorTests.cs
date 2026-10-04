using Npgsql;

namespace SqlAnalyst.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class QueryExecutorTests(DatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Answers_the_churn_question()
    {
        var outcome = await db.Executor.RunAsync("run_query", """
            SELECT c.name, ch.churned_on, ch.churn_reason, ch.lost_mrr
            FROM customer_churn ch JOIN customers c USING (customer_id)
            WHERE ch.churned_on >= date_trunc('quarter', current_date) - interval '3 months'
              AND ch.churned_on < date_trunc('quarter', current_date)
            ORDER BY ch.lost_mrr DESC
            """, maxRows: 10, purpose: "test", Ct);

        Assert.False(outcome.IsError, outcome.Text);
        Assert.Contains("<untrusted-data", outcome.Text);
        Assert.Contains("name | churned_on | churn_reason | lost_mrr", outcome.Text);
    }

    [Fact]
    public async Task Limits_rows_from_outside_the_query()
    {
        var outcome = await db.Executor.RunAsync("run_query", "SELECT * FROM invoices", maxRows: 3, purpose: null, Ct);

        Assert.False(outcome.IsError, outcome.Text);
        Assert.Contains("Строк: 3 (есть ещё строки", outcome.Text);
    }

    [Fact]
    public async Task Masks_personal_data_at_the_database_level()
    {
        var outcome = await db.Executor.RunAsync("run_query", "SELECT email_masked FROM customers WHERE customer_id = 1", 1, null, Ct);

        Assert.Contains("c***@client1.example", outcome.Text);
        Assert.DoesNotContain("contact1@", outcome.Text);
    }

    [Fact]
    public async Task Hides_injection_payloads_stored_in_the_data()
    {
        var outcome = await db.Executor.RunAsync("run_query",
            "SELECT ticket_id, body FROM support_tickets WHERE body ILIKE '%instruct%' OR body ILIKE '%инструкци%' OR body ILIKE '%SYSTEM:%'", 10, null, Ct);

        Assert.False(outcome.IsError, outcome.Text);
        Assert.DoesNotContain("IGNORE ALL PREVIOUS", outcome.Text);
        Assert.DoesNotContain("Забудь все", outcome.Text);
        Assert.DoesNotContain("Отток нулевой", outcome.Text);
        Assert.Contains("похожие на prompt injection (3)", outcome.Text);
    }

    [Fact]
    public async Task Hides_injection_payloads_echoed_in_error_messages()
    {
        var outcome = await db.Executor.RunAsync("run_query", "SELECT name::int FROM customers WHERE customer_id = 7", 1, null, Ct);

        Assert.True(outcome.IsError);
        Assert.DoesNotContain("игнорируй", outcome.Text);
    }

    [Fact]
    public async Task Blocks_expensive_plans_before_running_them()
    {
        var outcome = await db.Executor.RunAsync("run_query",
            "SELECT count(*) FROM invoices a, invoices b, invoices c", 1, null, Ct);

        Assert.True(outcome.IsError);
        Assert.Contains("слишком тяжёлый", outcome.Text);
    }

    [Fact]
    public async Task Statement_timeout_stops_what_the_plan_estimate_misses()
    {
        // Границы из подзапросов: планировщик не знает их и оценивает каждый generate_series в 1000 строк,
        // а на деле это 10^10 строк. EXPLAIN такой запрос пропустит, а таймаут — нет
        var outcome = await db.Executor.RunAsync("run_query",
            "SELECT count(*) FROM generate_series(1, (SELECT 100000)) a, generate_series(1, (SELECT 100000)) b", 1, null, Ct);

        Assert.True(outcome.IsError);
        Assert.Contains("таймауту", outcome.Text);
    }

    [Fact]
    public async Task Temp_file_limit_stops_queries_that_spill_to_disk()
    {
        var outcome = await db.Executor.RunAsync("run_query",
            "SELECT count(*) FROM generate_series(1, (SELECT 2000000000))", 1, null, Ct);

        Assert.True(outcome.IsError);
        Assert.Contains("лимит ресурсов", outcome.Text);
    }

    [Fact]
    public async Task Blocks_results_that_contain_a_canary_token()
    {
        var outcome = await db.Executor.RunAsync("run_query", "SELECT 'leak: CANARY-7f3a9c2e' AS x", 1, null, Ct);

        Assert.True(outcome.IsError);
        Assert.Contains("заблокирован", outcome.Text);
        Assert.DoesNotContain("CANARY", outcome.Text);
    }

    [Fact]
    public async Task Guard_rejects_before_reaching_the_database()
    {
        var outcome = await db.Executor.RunAsync("run_query", "SELECT * FROM internal.api_keys", 1, null, Ct);

        Assert.True(outcome.IsError);
        Assert.Contains("Схема internal недоступна", outcome.Text);
    }

    // Ниже — проверки последнего рубежа: что будет, если валидатор и обёртка всё же что-то пропустят.
    // Обращаемся к БД напрямую под ролью аналитика, мимо всех проверок приложения.

    [Theory]
    [InlineData("CREATE TABLE analytics.t (x int)", PostgresErrorCodes.ReadOnlySqlTransaction)]
    [InlineData("SELECT * FROM internal.api_keys", PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData("SELECT * FROM shop.customers", PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData("SELECT pg_sleep(0.1)", PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData("SELECT set_config('default_transaction_read_only', 'off', false)", PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData("SELECT query_to_xml('select 1', true, true, '')", PostgresErrorCodes.InsufficientPrivilege)]
    [InlineData("SELECT pg_read_file('/etc/passwd')", PostgresErrorCodes.InsufficientPrivilege)]
    public async Task Database_role_refuses_even_without_application_checks(string sql, string sqlState)
    {
        Assert.SkipWhen(DatabaseFixture.AnalystConnection is null, "Нет БД.");
        await using var connection = new NpgsqlConnection(DatabaseFixture.AnalystConnection);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        Assert.Equal(sqlState, ex.SqlState);
    }

    [Fact]
    public async Task Read_only_cannot_be_switched_off_inside_a_session()
    {
        Assert.SkipWhen(DatabaseFixture.AnalystConnection is null, "Нет БД.");
        await using var connection = new NpgsqlConnection(DatabaseFixture.AnalystConnection);
        await connection.OpenAsync(Ct);

        // Сессию переключить можно — но прав на запись у роли всё равно нет
        await using (var set = new NpgsqlCommand("SET default_transaction_read_only = off", connection))
        {
            await set.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand("CREATE TABLE analytics.t (x int)", connection);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }
}
