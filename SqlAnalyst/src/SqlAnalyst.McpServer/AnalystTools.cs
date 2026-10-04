using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SqlAnalyst.Core.Execution;

namespace SqlAnalyst.McpServer;

/// <summary>
/// Инструменты аналитика. Все только читают и не выходят за пределы БД (ReadOnly, OpenWorld = false) —
/// агент по аннотациям видит, что их можно вызывать без подтверждения человека.
/// </summary>
[McpServerToolType]
public sealed class AnalystTools(SchemaCatalog catalog, QueryExecutor executor, AnalystOptions options)
{
    [McpServerTool(Name = "list_tables", Title = "Таблицы витрины",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Список таблиц и представлений, доступных для анализа, с описаниями. Начинай работу с него.")]
    public async Task<CallToolResult> ListTables(CancellationToken cancellationToken = default)
    {
        var tables = await catalog.ListTablesAsync(cancellationToken);
        return Result(ToolOutcome.Ok(SchemaCatalog.FormatTables(tables, options.AllowedSchema)));
    }

    [McpServerTool(Name = "describe_table", Title = "Колонки таблицы",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Колонки таблицы или представления: имя, тип, NOT NULL и описание. Вызывай перед тем, как писать запрос к таблице.")]
    public async Task<CallToolResult> DescribeTable(
        [Description("Имя таблицы из list_tables, без схемы, например customers")] string table,
        CancellationToken cancellationToken = default)
    {
        var columns = await catalog.DescribeAsync(table, cancellationToken);

        return Result(columns is null
            ? ToolOutcome.Error($"Таблицы «{table}» нет в схеме {options.AllowedSchema}. Сверься со списком list_tables.")
            : ToolOutcome.Ok(SchemaCatalog.FormatColumns(table, columns)));
    }

    [McpServerTool(Name = "sample_rows", Title = "Примеры строк",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Несколько первых строк таблицы — чтобы понять формат значений (коды статусов, формат дат). Для ответов на вопросы используй run_query.")]
    public async Task<CallToolResult> SampleRows(
        [Description("Имя таблицы из list_tables, без схемы")] string table,
        [Description("Сколько строк вернуть, от 1 до 20")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (!await catalog.ExistsAsync(table, cancellationToken))
        {
            return Result(ToolOutcome.Error($"Таблицы «{table}» нет в схеме {options.AllowedSchema}. Сверься со списком list_tables."));
        }

        // Имя таблицы уже сверено с каталогом и всё равно экранируется
        var sql = $"SELECT * FROM {QueryExecutor.QuoteIdentifier(options.AllowedSchema)}.{QueryExecutor.QuoteIdentifier(table)}";
        return Result(await executor.RunAsync("sample_rows", sql, Math.Clamp(limit, 1, 20), purpose: null, cancellationToken));
    }

    [McpServerTool(Name = "run_query", Title = "Выполнить SQL",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Выполняет один SELECT (PostgreSQL) в транзакции только для чтения и возвращает таблицу результата.
        Ограничения: только SELECT или WITH ... SELECT, один оператор, только таблицы из list_tables, без системных каталогов
        и служебных функций; таймаут и лимит стоимости плана; не больше maxRows строк.
        Агрегируй на стороне БД (GROUP BY, COUNT, SUM) вместо выгрузки сырых строк.
        Если запрос отклонён, прочитай причину и перепиши запрос, а не повторяй его.
        """)]
    public async Task<CallToolResult> RunQuery(
        [Description("Текст запроса: один SELECT или WITH ... SELECT")] string sql,
        [Description("Зачем нужен запрос, одной фразой — пишется в журнал аудита")] string purpose,
        [Description("Максимум строк в ответе, от 1 до 500")] int maxRows = 100,
        CancellationToken cancellationToken = default) =>
        Result(await executor.RunAsync("run_query", sql, maxRows, purpose, cancellationToken));

    private static CallToolResult Result(ToolOutcome outcome) => new()
    {
        IsError = outcome.IsError,
        Content = [new TextContentBlock { Text = outcome.Text }],
    };
}
