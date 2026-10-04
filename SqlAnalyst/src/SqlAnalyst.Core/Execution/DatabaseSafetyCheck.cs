using Npgsql;

namespace SqlAnalyst.Core.Execution;

public sealed class UnsafeConnectionException(IReadOnlyList<string> problems)
    : Exception("Подключение к БД небезопасно для аналитика:\n- " + string.Join("\n- ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Fail closed: при старте сервер проверяет, что роль действительно ограничена. Если в строке подключения
/// случайно оказался суперпользователь или роль с правом записи — сервер не запустится, а не «будет осторожен».
/// </summary>
public static class DatabaseSafetyCheck
{
    public static async Task VerifyAsync(NpgsqlDataSource dataSource, string allowedSchema, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using (var role = new NpgsqlCommand(
            "SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls, rolreplication FROM pg_roles WHERE rolname = current_user",
            connection))
        await using (var reader = await role.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            var name = reader.GetString(0);
            string[] flags = ["SUPERUSER", "CREATEROLE", "CREATEDB", "BYPASSRLS", "REPLICATION"];

            for (var i = 0; i < flags.Length; i++)
            {
                if (reader.GetBoolean(i + 1))
                {
                    problems.Add($"роль {name} имеет атрибут {flags[i]}");
                }
            }
        }

        var readOnly = await ScalarAsync<string>(connection, "SHOW default_transaction_read_only", cancellationToken);
        if (readOnly != "on")
        {
            problems.Add("у роли не включён default_transaction_read_only (ALTER ROLE ... SET default_transaction_read_only = on)");
        }

        var timeout = await ScalarAsync<string>(connection, "SHOW statement_timeout", cancellationToken);
        if (timeout is "0" or null)
        {
            problems.Add("у роли не задан statement_timeout");
        }

        // Членство в ролях: pg_write_all_data, pg_read_server_files и т. п. перечеркнули бы все ограничения
        var memberships = await ListAsync(connection, """
            SELECT r.rolname FROM pg_auth_members m
            JOIN pg_roles r ON r.oid = m.roleid
            WHERE m.member = (SELECT oid FROM pg_roles WHERE rolname = current_user)
            """, cancellationToken);
        problems.AddRange(memberships.Select(r => $"роль входит в роль {r}"));

        var writable = await ListAsync(connection, """
            SELECT n.nspname || '.' || c.relname || ' (' || p.privilege || ')'
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            CROSS JOIN unnest(ARRAY['INSERT', 'UPDATE', 'DELETE', 'TRUNCATE']) AS p(privilege)
            WHERE c.relkind IN ('r', 'p', 'v', 'm') AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND has_table_privilege(c.oid, p.privilege)
            LIMIT 5
            """, cancellationToken);
        problems.AddRange(writable.Select(t => $"есть право записи в {t}"));

        var creatable = await ListAsync(connection,
            "SELECT nspname FROM pg_namespace WHERE has_schema_privilege(oid, 'CREATE')", cancellationToken);
        problems.AddRange(creatable.Select(s => $"есть право CREATE в схеме {s}"));

        var foreign = await ListAsync(connection, """
            SELECT n.nspname FROM pg_namespace n
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast') AND n.nspname <> $1
              AND n.nspname NOT LIKE 'pg\_temp\_%' AND n.nspname NOT LIKE 'pg\_toast\_temp\_%'
              AND has_schema_privilege(n.oid, 'USAGE')
            """, cancellationToken, allowedSchema);
        problems.AddRange(foreign.Select(s => $"роль видит схему {s} (должна видеть только {allowedSchema})"));

        if (problems.Count > 0)
        {
            throw new UnsafeConnectionException(problems);
        }
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params object[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
