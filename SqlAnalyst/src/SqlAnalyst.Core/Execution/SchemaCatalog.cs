using System.Text;
using Npgsql;
using SqlAnalyst.Core.Output;

namespace SqlAnalyst.Core.Execution;

public sealed record TableInfo(string Name, string Kind, string? Comment);

public sealed record ColumnInfo(string Name, string Type, bool Nullable, string? Comment);

/// <summary>
/// Метаданные витрины. Запросы к каталогу пишет сервер, а не модель, поэтому они идут мимо SqlGuard;
/// имена таблиц от модели принимаются только если они есть в списке, полученном из каталога.
/// Комментарии к таблицам тоже проходят через ResultFormatter.Sanitize: их пишут люди, а читает модель.
/// </summary>
public sealed class SchemaCatalog(NpgsqlDataSource dataSource, AnalystOptions options)
{
    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.relname, CASE c.relkind WHEN 'v' THEN 'view' WHEN 'm' THEN 'materialized view' ELSE 'table' END,
                   obj_description(c.oid, 'pg_class')
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = $1 AND c.relkind IN ('r', 'v', 'm', 'p') AND has_table_privilege(c.oid, 'SELECT')
            ORDER BY c.relname
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(options.AllowedSchema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tables = new List<TableInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(new TableInfo(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return tables;
    }

    public async Task<IReadOnlyList<ColumnInfo>?> DescribeAsync(string table, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(table, cancellationToken))
        {
            return null;
        }

        const string sql = """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull, col_description(c.oid, a.attnum)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE n.nspname = $1 AND c.relname = $2
            ORDER BY a.attnum
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(options.AllowedSchema);
        command.Parameters.AddWithValue(table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new List<ColumnInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return columns;
    }

    public async Task<bool> ExistsAsync(string table, CancellationToken cancellationToken) =>
        (await ListTablesAsync(cancellationToken)).Any(t => t.Name == table);

    /// <summary>Все схемы базы — для SqlGuard: обращение к любой, кроме разрешённой, запрещено.</summary>
    public async Task<IReadOnlySet<string>> ListSchemasAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT nspname FROM pg_namespace");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var schemas = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }

    public static string FormatTables(IReadOnlyList<TableInfo> tables, string schema)
    {
        var text = new StringBuilder($"Схема {schema}: {tables.Count} объектов. Обращайся к ним без префикса схемы или как {schema}.<имя>.\n");

        foreach (var table in tables)
        {
            text.Append($"- {table.Name} ({table.Kind})");
            if (table.Comment is { Length: > 0 } comment)
            {
                text.Append(" — ").Append(ResultFormatter.Sanitize(comment, 600));
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    public static string FormatColumns(string table, IReadOnlyList<ColumnInfo> columns)
    {
        var text = new StringBuilder($"{table}: {columns.Count} колонок\n");

        foreach (var column in columns)
        {
            text.Append($"- {column.Name} {column.Type}{(column.Nullable ? "" : " NOT NULL")}");
            if (column.Comment is { Length: > 0 } comment)
            {
                text.Append(" — ").Append(ResultFormatter.Sanitize(comment, 300));
            }

            text.Append('\n');
        }

        return text.ToString();
    }
}
