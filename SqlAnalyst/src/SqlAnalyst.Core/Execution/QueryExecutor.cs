using System.Collections;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using Npgsql;
using SqlAnalyst.Core.Guard;
using SqlAnalyst.Core.Output;

namespace SqlAnalyst.Core.Execution;

/// <summary>
/// Конвейер выполнения запроса модели. Рубежи по порядку:
/// 1) лимит частоты запросов;
/// 2) SqlGuard — статическая проверка текста (только один SELECT, без опасных слов, функций и схем);
/// 3) транзакция READ ONLY с локальными таймаутами — даже пропущенная запись упадёт в самой БД;
/// 4) PlanInspector — EXPLAIN до выполнения: системные каталоги, запрещённые функции, стоимость;
/// 5) обёртка SELECT * FROM (...) LIMIT n+1 — лимит строк навязывается снаружи;
/// 6) права роли в БД (см. db/init/04_roles.sql) — работают всегда, что бы ни пропустили рубежи выше;
/// 7) canary-проверка и ResultFormatter — что и в каком виде увидит модель.
/// Транзакция всегда откатывается.
/// </summary>
public sealed class QueryExecutor(
    NpgsqlDataSource dataSource,
    AnalystOptions options,
    SqlGuard guard,
    ResultFormatter formatter,
    AuditLog audit,
    QueryRateLimiter rateLimiter,
    TimeProvider time)
{
    public async Task<ToolOutcome> RunAsync(string tool, string sql, int? maxRows, string? purpose, CancellationToken cancellationToken)
    {
        if (!rateLimiter.TryAcquire())
        {
            Audit(tool, sql, purpose, "blocked", "rate limit");
            return ToolOutcome.Error($"Превышен лимит: не больше {options.QueriesPerMinute} запросов в минуту. Объедини вопросы в меньшее число запросов.");
        }

        var verdict = guard.Check(sql);
        if (!verdict.Allowed)
        {
            Audit(tool, sql, purpose, "blocked", verdict.Reason);
            return ToolOutcome.Error($"Запрос отклонён: {verdict.Reason}");
        }

        var limit = Math.Clamp(maxRows ?? options.DefaultMaxRows, 1, options.HardMaxRows);
        var wrapped = $"SELECT * FROM (\n{verdict.Sql}\n) AS analyst_query LIMIT {limit + 1}";
        var stopwatch = Stopwatch.StartNew();
        double? planCost = null;

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

            await using (var setup = new NpgsqlCommand(
                $"""
                SET TRANSACTION READ ONLY;
                SET LOCAL statement_timeout = '{options.StatementTimeoutSeconds}s';
                SET LOCAL lock_timeout = '1s';
                SET LOCAL search_path = {QuoteIdentifier(options.AllowedSchema)};
                """, connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(cancellationToken);
            }

            PlanSummary plan;
            await using (var explain = new NpgsqlCommand("EXPLAIN (FORMAT JSON, VERBOSE) " + wrapped, connection, transaction))
            {
                plan = PlanInspector.Parse((string)(await explain.ExecuteScalarAsync(cancellationToken))!);
            }

            planCost = plan.TotalCost;

            if (PlanInspector.Check(plan, options.MaxPlanCost) is { } planProblem)
            {
                Audit(tool, sql, purpose, "blocked", planProblem, planCost: planCost);
                return ToolOutcome.Error($"Запрос отклонён: {planProblem}");
            }

            var result = await ReadAsync(wrapped, limit, connection, transaction, stopwatch, plan.TotalCost, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);

            if (FindCanary(result) is { } canary)
            {
                Audit(tool, sql, purpose, "security", $"canary token {canary} in result", result.Rows.Count, stopwatch.ElapsedMilliseconds, planCost);
                return ToolOutcome.Error("Результат заблокирован: в нём обнаружены данные из закрытой части базы. Инцидент записан в журнал аудита.");
            }

            var formatted = formatter.Format(result, $"Запрос выполнен (стоимость плана {plan.TotalCost.ToString("F0", CultureInfo.InvariantCulture)}).");
            Audit(tool, sql, purpose, "ok", null, result.Rows.Count, stopwatch.ElapsedMilliseconds, planCost, formatted.Flags);

            return ToolOutcome.Ok(formatted.Text);
        }
        catch (PostgresException ex)
        {
            var (verdictName, message) = ex.SqlState switch
            {
                PostgresErrorCodes.QueryCanceled => ("blocked",
                    $"Запрос прерван по таймауту ({options.StatementTimeoutSeconds} с). Добавь фильтры по дате, агрегируй или ограничь соединения."),
                PostgresErrorCodes.ConfigurationLimitExceeded or PostgresErrorCodes.OutOfMemory or PostgresErrorCodes.DiskFull => ("blocked",
                    "Запрос превысил лимит ресурсов (память или временные файлы). Агрегируй данные и сократи промежуточные результаты."),
                PostgresErrorCodes.InsufficientPrivilege => ("security",
                    $"Нет доступа: роль аналитика видит только схему {options.AllowedSchema}. Используй list_tables."),
                PostgresErrorCodes.ReadOnlySqlTransaction => ("security",
                    "База открыта только на чтение: изменяющие операции невозможны."),
                _ => ("error",
                    $"Ошибка PostgreSQL {ex.SqlState}: {formatter.WrapUntrustedMessage(ex.MessageText)}"
                    + (ex.Hint is { Length: > 0 } hint ? $" Подсказка: {formatter.WrapUntrustedMessage(hint)}" : "")),
            };

            Audit(tool, sql, purpose, verdictName, $"{ex.SqlState}: {ex.MessageText}", durationMs: stopwatch.ElapsedMilliseconds, planCost: planCost);
            return ToolOutcome.Error(message);
        }
    }

    public static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static async Task<QueryResult> ReadAsync(
        string sql, int limit, NpgsqlConnection connection, NpgsqlTransaction transaction,
        Stopwatch stopwatch, double planCost, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => new QueryColumn(reader.GetName(i), reader.GetDataTypeName(i)))
            .ToList();

        var rows = new List<string?[]>();
        var truncated = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == limit)
            {
                truncated = true;
                break;
            }

            var row = new string?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = ReadValue(reader, i);
            }

            rows.Add(row);
        }

        return new QueryResult(columns, rows, truncated, stopwatch.ElapsedMilliseconds, planCost);
    }

    private static string? ReadValue(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        try
        {
            return FormatValue(reader.GetValue(ordinal));
        }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException)
        {
            return $"[тип {reader.GetDataTypeName(ordinal)} не поддерживается]";
        }
    }

    internal static string FormatValue(object value) => value switch
    {
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        byte[] bytes => $"[bytea, {bytes.Length} байт]",
        string s => s,
        IEnumerable items => "{" + string.Join(",", items.Cast<object?>().Select(item => item is null ? "NULL" : FormatValue(item))) + "}",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private string? FindCanary(QueryResult result)
    {
        if (options.CanaryTokens.Count == 0)
        {
            return null;
        }

        foreach (var token in options.CanaryTokens)
        {
            if (result.Columns.Any(c => c.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
                || result.Rows.Any(row => row.Any(cell => cell?.Contains(token, StringComparison.OrdinalIgnoreCase) == true)))
            {
                return token;
            }
        }

        return null;
    }

    private void Audit(
        string tool, string? sql, string? purpose, string verdict, string? reason,
        int? rows = null, long? durationMs = null, double? planCost = null, IReadOnlyList<string>? flags = null) =>
        audit.Write(new AuditRecord(time.GetUtcNow(), tool, sql, purpose, verdict, reason, rows, durationMs, planCost,
            flags is { Count: > 0 } ? flags : null));
}
