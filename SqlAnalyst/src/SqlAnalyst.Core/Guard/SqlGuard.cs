namespace SqlAnalyst.Core.Guard;

public sealed record GuardVerdict(bool Allowed, string? Reason, string? Sql)
{
    public static GuardVerdict Allow(string sql) => new(true, null, sql);

    public static GuardVerdict Deny(string reason) => new(false, reason, null);
}

/// <summary>
/// Статическая проверка SQL до отправки в базу. Это первый, «объясняющий» рубеж: он отсекает очевидно
/// опасное и возвращает модели понятную причину, чтобы та могла переписать запрос.
/// Безопасность держится не на нём одном: дальше идут read-only транзакция, проверка плана (PlanInspector)
/// и права роли в самой БД. Поэтому правила здесь — консервативный denylist, а не полный парсер SQL.
/// </summary>
public sealed class SqlGuard(GuardPolicy policy)
{
    // Всё, что меняет данные, схему, права, сессию или выполняет код. В SELECT-запросе этим словам вне строк
    // и кавычек взяться неоткуда, так что ложных срабатываний почти нет.
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.Ordinal)
    {
        "insert", "update", "delete", "merge", "truncate",
        "create", "alter", "drop", "grant", "revoke",
        "copy", "call", "do", "execute", "prepare", "deallocate",
        "set", "reset", "show", "discard", "load", "import",
        "lock", "vacuum", "analyze", "analyse", "cluster", "reindex", "refresh", "checkpoint",
        "listen", "notify", "unlisten",
        // SELECT ... INTO создаёт таблицу
        "into",
    };

    // Функции, которые спят, читают файлы сервера, ходят по сети, меняют настройки, берут блокировки
    // или выполняют SQL из строки (query_to_xml обошёл бы валидатор целиком).
    private static readonly HashSet<string> ForbiddenFunctions = new(StringComparer.Ordinal)
    {
        "set_config", "current_setting", "query_to_xml", "query_to_xml_and_xmlschema", "query_to_xmlschema",
        "cursor_to_xml", "cursor_to_xmlschema", "table_to_xml", "table_to_xml_and_xmlschema", "table_to_xmlschema",
        "schema_to_xml", "schema_to_xml_and_xmlschema", "schema_to_xmlschema",
        "database_to_xml", "database_to_xml_and_xmlschema", "database_to_xmlschema",
        "txid_current", "txid_current_snapshot", "inet_server_addr", "inet_server_port", "version",
    };

    private static readonly string[] ForbiddenFunctionPrefixes = ["pg_", "lo_", "dblink", "has_"];

    // Служебные схемы закрыты всегда, независимо от настроек
    private static readonly string[] SystemSchemas = ["pg_catalog", "information_schema", "pg_toast"];

    public GuardVerdict Check(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardVerdict.Deny("Пустой запрос.");
        }

        if (sql.Length > policy.MaxSqlLength)
        {
            return GuardVerdict.Deny($"Запрос длиннее {policy.MaxSqlLength} символов. Упрости его.");
        }

        if (sql.Contains('\0'))
        {
            return GuardVerdict.Deny("Запрос содержит нулевой символ.");
        }

        IReadOnlyList<SqlToken> tokens;
        try
        {
            tokens = SqlTokenizer.Tokenize(sql);
        }
        catch (SqlTokenizeException ex)
        {
            return GuardVerdict.Deny($"Не удалось разобрать запрос: {ex.Message}");
        }

        // Точки с запятой в конце допустимы и отбрасываются; любая другая — второй оператор
        var end = tokens.Count;
        while (end > 0 && tokens[end - 1].IsPunctuation(';'))
        {
            end--;
        }

        if (end == 0)
        {
            return GuardVerdict.Deny("Пустой запрос.");
        }

        var body = tokens.Take(end).ToList();

        if (body.Any(t => t.IsPunctuation(';')))
        {
            return GuardVerdict.Deny("Разрешён только один оператор SELECT: уберите ';' внутри запроса.");
        }

        var first = body[0];
        if (!(first.IsWord("select") || first.IsWord("with") || first.IsPunctuation('(')))
        {
            return GuardVerdict.Deny("Разрешены только запросы на чтение: SELECT или WITH ... SELECT.");
        }

        var depth = 0;

        for (var i = 0; i < body.Count; i++)
        {
            var token = body[i];
            var next = i + 1 < body.Count ? body[i + 1] : null;

            switch (token.Kind)
            {
                case SqlTokenKind.Parameter:
                    return GuardVerdict.Deny("Параметры вида $1 не поддерживаются: подставь значения прямо в запрос.");

                case SqlTokenKind.Punctuation when token.Value == "(":
                    depth++;
                    break;

                case SqlTokenKind.Punctuation when token.Value == ")":
                    // Лишняя закрывающая скобка вырвалась бы из обёртки SELECT * FROM (...) LIMIT n
                    if (--depth < 0)
                    {
                        return GuardVerdict.Deny("Несбалансированные скобки.");
                    }

                    break;

                case SqlTokenKind.Word when ForbiddenKeywords.Contains(token.Value):
                    return GuardVerdict.Deny($"Ключевое слово {token.Value.ToUpperInvariant()} запрещено: доступно только чтение (SELECT).");

                case SqlTokenKind.Word when token.Value == "for" && next is { Kind: SqlTokenKind.Word } && next.Value is "share" or "no" or "key":
                    return GuardVerdict.Deny("Блокирующие чтения (FOR SHARE / FOR UPDATE) запрещены.");
            }

            if (token.IdentifierName is not { } name)
            {
                continue;
            }

            var isFunctionCall = next is not null && next.IsPunctuation('(');
            if (isFunctionCall && IsForbiddenFunction(name))
            {
                return GuardVerdict.Deny($"Функция {name}() запрещена политикой безопасности.");
            }

            var isQualifier = next is not null && next.IsPunctuation('.');
            if (isQualifier && IsForbiddenSchema(name))
            {
                return GuardVerdict.Deny($"Схема {name} недоступна. Используй только таблицы схемы {policy.AllowedSchema} (list_tables).");
            }

            // Системные каталоги (pg_user, pg_stat_activity, ...) видны и без схемы — через search_path
            if (!isFunctionCall && (name.StartsWith("pg_", StringComparison.Ordinal) || name == "information_schema"))
            {
                return GuardVerdict.Deny($"Системные каталоги ({name}) недоступны. Структуру данных смотри через list_tables и describe_table.");
            }
        }

        if (depth != 0)
        {
            return GuardVerdict.Deny("Несбалансированные скобки.");
        }

        var normalized = sql[..(end < tokens.Count ? tokens[end].Position : sql.Length)].TrimEnd();
        return GuardVerdict.Allow(normalized);
    }

    private static bool IsForbiddenFunction(string name) =>
        ForbiddenFunctions.Contains(name)
        || name.Contains("_to_xml", StringComparison.Ordinal)
        || ForbiddenFunctionPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private bool IsForbiddenSchema(string name) =>
        SystemSchemas.Contains(name, StringComparer.Ordinal)
        || (!string.Equals(name, policy.AllowedSchema, StringComparison.Ordinal) && policy.KnownSchemas.Contains(name));
}

public sealed class GuardPolicy
{
    /// <summary>Единственная схема, к таблицам которой может обращаться модель.</summary>
    public string AllowedSchema { get; init; } = "analytics";

    /// <summary>Все схемы базы. Обращение через точку к любой из них, кроме AllowedSchema, запрещено.</summary>
    public IReadOnlySet<string> KnownSchemas { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public int MaxSqlLength { get; init; } = 8000;
}
