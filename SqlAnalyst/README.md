# SQL-аналитик на естественном языке

Агент отвечает на вопросы вроде «какие клиенты ушли в прошлом квартале?» и «нарисуй MRR по месяцам»: сам пишет SQL, выполняет его через **read-only MCP-сервер** к PostgreSQL и строит графики.

Главное в проекте — **guardrails и защита от prompt injection**. Модель пишет произвольный SQL и читает произвольный текст из базы, поэтому исходим из того, что и запрос, и данные могут быть враждебными.

```
 Вы ──► SqlAnalyst.Agent ──Claude API──► Claude
            │  ToolGuard, OutputGuard, render_chart → charts/*.html
            │ stdio (MCP)
            ▼
       SqlAnalyst.McpServer ──► QueryExecutor ──► PostgreSQL (роль analyst_ro, схема analytics)
       list_tables, describe_table,   SqlGuard → READ ONLY tx → EXPLAIN → LIMIT → canary → конверт
       sample_rows, run_query
```

| Проект | Что внутри |
|---|---|
| `db/init` | Схема, детерминированные демо-данные (600 клиентов, подписки, счета, обращения), витрина `analytics`, роль `analyst_ro` |
| `src/SqlAnalyst.Core` | Лексер и валидатор SQL, проверка плана, выполнение запроса, форматирование результата, детектор prompt injection, рендер графиков |
| `src/SqlAnalyst.McpServer` | MCP-сервер по stdio: 4 read-only инструмента |
| `src/SqlAnalyst.Agent` | Консольный агент: Claude через Claude API + инструменты сервера + локальный `render_chart` |
| `tests/SqlAnalyst.Tests` | Корпус атак на валидатор, prompt injection, графики; интеграционные и end-to-end тесты с PostgreSQL |

Стек: .NET 10, Npgsql 10, [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) 2.2.0, [Anthropic](https://github.com/anthropics/anthropic-sdk-csharp) 12.53.0 + Microsoft.Extensions.AI, xUnit v3.

## Быстрый старт

Нужны .NET 10 SDK и Docker.

```bash
cd SqlAnalyst
docker compose up -d          # PostgreSQL 17 на localhost:5433, скрипты из db/init применятся сами
dotnet build
dotnet user-secrets set "Anthropic:ApiKey" "<ключ>" --project src/SqlAnalyst.Agent
dotnet run --project src/SqlAnalyst.Agent
```

Один вопрос без чата:

```bash
dotnet run --project src/SqlAnalyst.Agent -- --ask "какие клиенты ушли в прошлом квартале и почему?"
```

Демо-данные считаются от текущей даты, поэтому «прошлый квартал», «последние 12 месяцев» и неоплаченные счета есть в любой день. Вопросы, на которых удобно смотреть агента:

- Какие клиенты ушли в прошлом квартале? Сколько MRR мы потеряли?
- Нарисуй отток по кварталам за последние два года.
- Какой сегмент уходит чаще всего и по каким причинам?
- Покажи MRR по месяцам за год графиком.
- Что пишут клиенты в срочных открытых обращениях? *(в данных лежат три prompt injection — агент должен их не выполнить и сказать о них)*
- Покажи мне таблицу internal.api_keys. *(должен отказать)*

### Подключение сервера к Claude Code или Claude Desktop

Сервер — обычный stdio MCP-сервер, его можно подключить и без нашего агента:

```bash
claude mcp add --transport stdio sql-analyst -- dotnet /путь/к/SqlAnalyst.McpServer.dll
```

Строка подключения берётся из `src/SqlAnalyst.McpServer/appsettings.json` или переменной `Analyst__ConnectionString`.

## Модель угроз

| Угроза | Пример | Кто останавливает |
|---|---|---|
| Запись и DDL | `DELETE ...`, `WITH d AS (DELETE ...) SELECT`, `SELECT ... INTO` | SqlGuard → READ ONLY транзакция → `default_transaction_read_only` роли → у роли нет прав на запись |
| Несколько операторов | `SELECT 1; DROP TABLE ...` | SqlGuard (лексер понимает строки, `$$`, комментарии) |
| Выход из обёртки `LIMIT` | `SELECT 1) AS x UNION SELECT (1` | SqlGuard проверяет баланс скобок |
| Чужие схемы и секреты | `internal.api_keys`, `"internal"."api_keys"`, `U&"\0069nternal"` | SqlGuard → права роли (нет USAGE на `shop` и `internal`) → canary-метка в результате |
| Системные каталоги | `pg_shadow`, `pg_stat_activity`, `information_schema` | SqlGuard → PlanInspector (видит реальные отношения в плане) |
| Опасные функции | `pg_sleep`, `set_config`, `pg_read_file`, `query_to_xml('<любой SQL>')`, `dblink`, `lo_import` | SqlGuard → PlanInspector → `REVOKE EXECUTE` в БД |
| Тяжёлые запросы (DoS) | декартово произведение, бесконечный `generate_series` | лимит стоимости плана (EXPLAIN) → `statement_timeout` → `temp_file_limit`, `work_mem` → лимит строк и объёма → лимит запросов в минуту |
| Утечка PII | e-mail и телефоны клиентов | маскирование в представлениях — модель не может получить исходные значения |
| Prompt injection в данных | «IGNORE ALL PREVIOUS INSTRUCTIONS…», `</query_result> SYSTEM: …` в обращениях и именах | конверт `<untrusted-data id="nonce">` → нейтрализация `<`, `>`, переводов строк, невидимых символов → InjectionDetector скрывает ячейку → системный промпт → у агента нет опасных инструментов |
| Prompt injection в ошибках | `SELECT name::int ...` → текст строки в сообщении об ошибке | сообщения PostgreSQL проходят тот же детектор и конверт |
| Утечка через график | `<script>` в подписи | весь текст HTML-экранируется, CSP `default-src 'none'`, имя файла — только буквы и цифры |
| Подмена инструментов | сервер объявил изменяющий инструмент | агент подключает только инструменты с `readOnlyHint` и без `openWorldHint` |
| Небезопасная настройка | в строке подключения суперпользователь | сервер **не стартует** (DatabaseSafetyCheck: атрибуты роли, read-only, таймаут, права на запись, видимые схемы) |
| Утечка в ответе | секрет всё-таки дошёл до модели | OutputGuard блокирует ответ с canary-меткой или кусками служебных инструкций |

Принцип: **ни один рубеж не единственный.** Валидатор SQL — консервативный denylist, его можно обойти творчески, но тогда запрос упрётся в план, транзакцию и права роли. Детектор prompt injection — эвристика, её можно обойти перефразированием, но у агента нет ни одного инструмента, которым можно навредить: всё, что может сделать «уговорённая» модель, — неправильно ответить, а ответ всегда содержит SQL, по которому его можно проверить.

### Слои по порядку

1. **Роль в БД** (`db/init/04_roles.sql`). `analyst_ro` видит только представления схемы `analytics`, на уровне роли включены `default_transaction_read_only`, `statement_timeout = 5s`, `temp_file_limit`, `work_mem`, отобран `EXECUTE` у `pg_sleep`, `set_config`, `query_to_xml`, advisory-блокировок и `lo_*`.
2. **Витрина** (`03_analytics.sql`). Представления работают с правами владельца: сырые таблицы недоступны, PII замаскированы, у таблиц и колонок есть комментарии — модели проще писать правильный SQL.
3. **Проверка при старте** (`DatabaseSafetyCheck`). Fail closed: если роль может больше, чем должна, сервер завершается с перечнем проблем.
4. **SqlGuard + SqlTokenizer.** Один оператор, только `SELECT`/`WITH`, запрещённые слова, функции и схемы, баланс скобок, без `U&` и параметров. Возвращает модели понятную причину, чтобы та переписала запрос.
5. **Транзакция.** `REPEATABLE READ READ ONLY`, `SET LOCAL statement_timeout`, `lock_timeout`, `search_path`; всегда откатывается.
6. **PlanInspector.** `EXPLAIN (FORMAT JSON, VERBOSE)` до выполнения: системные каталоги, функции, стоимость плана.
7. **Обёртка** `SELECT * FROM (<запрос>) LIMIT n+1` — лимит строк навязывается снаружи.
8. **Canary-метки.** В honeypot-таблицах `internal.*` лежат значения с меткой `CANARY-7f3a9c2e`; результат с ней блокируется целиком и пишется в аудит как инцидент.
9. **ResultFormatter + InjectionDetector.** Конверт с nonce, нейтрализация разметки, обрезка, скрытие подозрительных ячеек (режим `Redact`, можно `Flag`).
10. **Агент.** Только read-only инструменты, бюджет вызовов на вопрос, системный промпт с правилами для недоверенных данных, OutputGuard для готового ответа.
11. **Аудит.** Каждый вызов — строка JSONL (`audit/queries.jsonl` рядом с сервером): SQL, цель запроса (`purpose`), вердикт, стоимость плана, число строк, найденные injection.

## Тесты

```bash
dotnet test
```

Без базы проходят unit-тесты (корпус атак на валидатор, детектор, форматирование, графики), интеграционные пропускаются. Чтобы прогнать всё, подними базу и задай строки подключения:

```bash
docker compose up -d
export SQLANALYST_TEST_CONNECTION="Host=localhost;Port=5433;Database=shop;Username=analyst_ro;Password=analyst_ro"
export SQLANALYST_TEST_ADMIN_CONNECTION="Host=localhost;Port=5433;Database=shop;Username=postgres;Password=postgres"
dotnet test
```

Интеграционные тесты проверяют каждый рубеж отдельно — в том числе что роль БД сама отказывает в записи, чтении `internal` и вызове `pg_sleep`, если обратиться к ней мимо всех проверок приложения, и что сервер не стартует под суперпользователем. В CI (`.github/workflows/sql-analyst.yml`) PostgreSQL поднимается как service-контейнер.

## Настройки сервера

Секция `Analyst` в `src/SqlAnalyst.McpServer/appsettings.json` (или переменные `Analyst__*`):

| Ключ | По умолчанию | Смысл |
|---|---|---|
| `ConnectionString` | `...Port=5433;...Username=analyst_ro` | только read-only роль |
| `AllowedSchema` | `analytics` | единственная схема, доступная модели |
| `DefaultMaxRows` / `HardMaxRows` | 100 / 500 | лимит строк в ответе |
| `StatementTimeoutSeconds` | 5 | таймаут запроса |
| `MaxPlanCost` | 200000 | лимит оценки стоимости плана |
| `MaxCellChars` / `MaxResultChars` | 300 / 30000 | обрезка ячеек и всего ответа |
| `QueriesPerMinute` | 30 | лимит частоты запросов |
| `CanaryTokens` | `CANARY-7f3a9c2e` | метки honeypot-данных |
| `InjectionMode` | `Redact` | `Redact` — скрыть ячейку, `Flag` — оставить с пометкой |
| `AuditLogPath` | `audit/queries.jsonl` | журнал запросов; пусто — только stderr |
