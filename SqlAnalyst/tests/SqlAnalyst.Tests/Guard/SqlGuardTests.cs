using SqlAnalyst.Core.Guard;

namespace SqlAnalyst.Tests.Guard;

public sealed class SqlGuardTests
{
    private static readonly SqlGuard Guard = new(new GuardPolicy
    {
        AllowedSchema = "analytics",
        KnownSchemas = new HashSet<string> { "analytics", "shop", "internal", "public", "pg_catalog", "information_schema" },
    });

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select count(*) from customers")]
    [InlineData("SELECT * FROM analytics.customers WHERE segment = 'SMB';")]
    [InlineData("WITH q AS (SELECT date_trunc('quarter', current_date) AS start) SELECT * FROM customer_churn, q WHERE churned_on >= q.start - interval '3 months'")]
    [InlineData("SELECT c.name, ch.churned_on FROM customer_churn ch JOIN customers c USING (customer_id) ORDER BY ch.lost_mrr DESC")]
    [InlineData("SELECT subject FROM support_tickets WHERE body ILIKE '%drop table%' -- текст в строке не ключевое слово")]
    [InlineData("SELECT 'DELETE FROM x; DROP TABLE y' AS s")]
    [InlineData("SELECT $$; drop table customers$$ AS s")]
    [InlineData("SELECT \"update\" FROM (SELECT 1 AS \"update\") t")]
    [InlineData("SELECT segment, sum(mrr) FILTER (WHERE cancelled_on IS NULL) FROM subscriptions s JOIN customers USING (customer_id) GROUP BY ROLLUP (segment)")]
    [InlineData("SELECT CASE WHEN mrr > 100 THEN 'big' ELSE 'small' END, extract(year FROM started_on) FROM subscriptions")]
    [InlineData("(SELECT 1) UNION ALL (SELECT 2)")]
    [InlineData("SELECT to_char(issued_on, 'YYYY-MM') AS month, sum(amount) FROM invoices GROUP BY 1 ORDER BY 1")]
    public void Allows_read_queries(string sql)
    {
        var verdict = Guard.Check(sql);

        Assert.True(verdict.Allowed, verdict.Reason);
    }

    // Корпус атак: каждая строка — то, что модель могла бы попробовать сама или под влиянием prompt injection
    [Theory]
    [InlineData("", "Пустой")]
    [InlineData(";;", "Пустой")]
    [InlineData("DROP TABLE customers", "только запросы на чтение")]
    [InlineData("DELETE FROM customers", "только запросы на чтение")]
    [InlineData("SELECT 1; DROP TABLE customers", "один оператор")]
    [InlineData("SELECT 1;\n-- ok\nDELETE FROM customers", "один оператор")]
    [InlineData("WITH d AS (DELETE FROM customers RETURNING *) SELECT * FROM d", "DELETE")]
    [InlineData("WITH u AS (UPDATE subscriptions SET mrr = 0 RETURNING 1) SELECT 1", "UPDATE")]
    [InlineData("SELECT * INTO stolen FROM customers", "INTO")]
    [InlineData("SELECT * FROM customers FOR UPDATE", "UPDATE")]
    [InlineData("SELECT * FROM customers FOR SHARE", "FOR SHARE")]
    [InlineData("SELECT * FROM customers FOR NO KEY UPDATE", "FOR SHARE")]
    [InlineData("COPY customers TO '/tmp/x'", "только запросы на чтение")]
    [InlineData("EXPLAIN ANALYZE SELECT 1", "только запросы на чтение")]
    [InlineData("SET statement_timeout = 0", "только запросы на чтение")]
    [InlineData("SELECT set_config('default_transaction_read_only', 'off', false)", "set_config")]
    [InlineData("SELECT current_setting('data_directory')", "current_setting")]
    [InlineData("SELECT pg_sleep(60)", "pg_sleep")]
    [InlineData("SELECT PG_SLEEP(60)", "pg_sleep")]
    [InlineData("SELECT \"pg_sleep\"(60)", "pg_sleep")]
    [InlineData("SELECT pg_catalog.pg_sleep(60)", "pg_catalog")]
    [InlineData("SELECT pg_read_file('/etc/passwd')", "pg_read_file")]
    [InlineData("SELECT lo_import('/etc/passwd')", "lo_import")]
    [InlineData("SELECT dblink('host=evil', 'select 1')", "dblink")]
    [InlineData("SELECT query_to_xml('select * from internal.api_keys', true, true, '')", "query_to_xml")]
    [InlineData("SELECT pg_terminate_backend(1)", "pg_terminate_backend")]
    [InlineData("SELECT pg_advisory_lock(1)", "pg_advisory_lock")]
    [InlineData("SELECT * FROM internal.api_keys", "internal")]
    [InlineData("SELECT * FROM \"internal\".\"api_keys\"", "internal")]
    [InlineData("SELECT * FROM INTERNAL . api_keys", "internal")]
    [InlineData("SELECT * FROM internal/**/.api_keys", "internal")]
    [InlineData("SELECT * FROM shop.customers", "shop")]
    [InlineData("SELECT * FROM public.anything", "public")]
    [InlineData("SELECT * FROM pg_catalog.pg_authid", "pg_catalog")]
    [InlineData("SELECT * FROM pg_shadow", "pg_shadow")]
    [InlineData("SELECT query FROM pg_stat_activity", "pg_stat_activity")]
    [InlineData("SELECT * FROM information_schema.tables", "information_schema")]
    [InlineData("SELECT * FROM U&\"\\0069nternal\".api_keys", "U&")]
    [InlineData("SELECT 1) AS q; SELECT (1", "один оператор")]
    [InlineData("SELECT 1) AS x UNION SELECT (1", "скобки")]
    [InlineData("SELECT * FROM customers WHERE customer_id = $1", "Параметры")]
    [InlineData("SELECT 'unterminated", "Незакрытый")]
    [InlineData("SELECT 1 /* unterminated", "Незакрытый")]
    [InlineData("/* comment */ DELETE FROM customers", "только запросы на чтение")]
    [InlineData("SELECT 1 -- ;\n; DELETE FROM customers", "один оператор")]
    public void Rejects_attacks(string sql, string expectedReason)
    {
        var verdict = Guard.Check(sql);

        Assert.False(verdict.Allowed, $"Пропущено: {sql}");
        Assert.Contains(expectedReason, verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_overlong_queries()
    {
        var verdict = Guard.Check("SELECT " + new string('1', 9000));

        Assert.False(verdict.Allowed);
    }

    [Fact]
    public void Strips_trailing_semicolons_and_keeps_comments_inside()
    {
        var verdict = Guard.Check("SELECT 1 -- комментарий\n;;  ");

        Assert.True(verdict.Allowed);
        Assert.Equal("SELECT 1 -- комментарий", verdict.Sql);
    }
}
