# CRM + MCP-сервер на .NET 10

Небольшая CRM (контакты, компании, сделки, история активностей, задачи) с MCP-сервером, через который с ней работает AI-агент: Claude Desktop, Claude Code, MCP Inspector или твой собственный клиент. Для людей и интеграций — REST и GraphQL API.

Главная идея: **MCP — тонкий адаптер над бизнес-логикой**. Инструменты вызывают те же сервисы, что и REST и GraphQL API, поэтому валидация, права и аудит одинаковы для человека и для агента.

```
 Клиент                               Хост                              Библиотеки
 Crm.Agent, Claude Code  ──HTTP──►  Crm.Api  POST /mcp ──────────►  Crm.Mcp ──► Crm.Core ──► PostgreSQL
 фронтенд, интеграции    ──REST──►  Crm.Api  /api/*   ──────────────────────────► Crm.Core
 фронтенд, Nitro IDE     ─GraphQL►  Crm.Api  /graphql ───────────►  Crm.GraphQL ──► Crm.Core
 Claude Desktop, Cursor  ──stdio─►  Crm.Mcp.Stdio ───────────────►  Crm.Mcp ──► Crm.Core
```

| Проект | Что внутри |
|---|---|
| `src/Crm.Core` | Домен, EF Core (`CrmDbContext`), сервисы с проверкой прав, аудит, демо-данные |
| `src/Crm.Mcp` | MCP-инструменты, ресурсы, промпты, форматирование ответов, фильтры (ошибки + аудит) |
| `src/Crm.GraphQL` | GraphQL-схема на Hot Chocolate: типы, DataLoader, фильтры, мутации, подписки |
| `src/Crm.Api` | ASP.NET Core: `/mcp` (Streamable HTTP, stateless), `/api` (REST), `/graphql`, аутентификация по API-ключу |
| `src/Crm.Mcp.Stdio` | Консольный MCP-сервер по stdio для локальных клиентов |
| `src/Crm.Agent` | Свой агент: консольный чат с Claude через Claude API, инструменты берёт с MCP-сервера CRM |
| `tests/Crm.GraphQL.Tests` | Интеграционные тесты GraphQL API: xUnit v3, WebApplicationFactory, PostgreSQL |

Стек: .NET 10, ASP.NET Core, EF Core 10 + Npgsql (PostgreSQL), [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) 2.2.0 — официальный C# SDK, [Anthropic](https://github.com/anthropics/anthropic-sdk-csharp) 12.53.0 + Microsoft.Extensions.AI — для агента, [Hot Chocolate](https://chillicream.com/docs/hotchocolate) 16 — GraphQL-сервер.

## Быстрый старт

Нужны .NET 10 SDK и Docker.

```bash
docker compose up -d                  # PostgreSQL на localhost:5432 (crm/crm)
dotnet run --project src/Crm.Api      # http://localhost:5080
```

При первом запуске создаётся схема БД и демо-данные: 2 пользователя, 4 компании, 6 контактов, 8 сделок, история и задачи (в том числе просроченные и на сегодня). Проверка:

```bash
curl http://localhost:5080/api/pipeline -H "Authorization: Bearer anna-dev-key"
```

В Windows PowerShell пиши `curl.exe` или открой `src/Crm.Api/Crm.Api.http` в Visual Studio / Rider — там готовые REST- и MCP-запросы.

| Пользователь | Роль | API-ключ (только для разработки) |
|---|---|---|
| anna@crm.local, Анна Смирнова | Admin | `anna-dev-key` |
| boris@crm.local, Борис Козлов | Manager | `boris-dev-key` |

## Подключение к Claude Desktop (stdio)

1. Собери проект: `dotnet build src/Crm.Mcp.Stdio`. PostgreSQL должен быть запущен.
2. В Claude Desktop: Settings → Developer → Edit Config (файл `%APPDATA%\Claude\claude_desktop_config.json`) и добавь сервер:

```json
{
  "mcpServers": {
    "crm": {
      "command": "dotnet",
      "args": ["C:\\Users\\<you>\\source\\repos\\CrmMcp\\src\\Crm.Mcp.Stdio\\bin\\Debug\\net10.0\\Crm.Mcp.Stdio.dll"],
      "env": { "Crm__StdioUserEmail": "anna@crm.local" }
    }
  }
}
```

3. Полностью закрой и снова запусти Claude Desktop. Логи сервера: `%APPDATA%\Claude\logs\mcp-server-crm.log`.

Запускаем готовую dll, а не `dotnet run`: `dotnet run` сначала собирает проект и может писать в stdout, а stdout у stdio-сервера занят протоколом. По той же причине все логи сервера идут в stderr. После изменений в коде пересобери проект и перезапусти Claude Desktop.

От чьего имени работает агент, задаёт `Crm__StdioUserEmail`. Поставь `boris@crm.local` и проверь, что чужие сделки менять нельзя.

## Подключение к Claude Code

По HTTP (Crm.Api должен быть запущен):

```bash
claude mcp add --transport http crm http://localhost:5080/mcp --header "Authorization: Bearer anna-dev-key"
```

Или по stdio:

```bash
claude mcp add --transport stdio --env Crm__StdioUserEmail=anna@crm.local crm-local -- dotnet /путь/к/Crm.Mcp.Stdio.dll
```

Проверить подключение: `/mcp` внутри Claude Code или `claude mcp list`.

## MCP Inspector

```bash
npx @modelcontextprotocol/inspector
```

Транспорт Streamable HTTP, URL `http://localhost:5080/mcp`, заголовок `Authorization: Bearer anna-dev-key`. Удобно смотреть схемы инструментов и вызывать их руками.

## Свой агент: Crm.Agent

Консольный чат, в котором ты сам собираешь то, что делает Claude Desktop: Claude через Claude API, инструменты CRM через MCP-клиент. Нужен ключ Claude API — его создают в Claude Console (platform.claude.com). API оплачивается отдельно от подписки Claude.

```bash
dotnet user-secrets set "Anthropic:ApiKey" "<ключ>" --project src/Crm.Agent
dotnet run --project src/Crm.Api       # в отдельном окне: агент подключается к /mcp по HTTP
dotnet run --project src/Crm.Agent     # или с флагом -- --stdio: агент сам запустит Crm.Mcp.Stdio
```

Вместо user-secrets можно задать переменную окружения `ANTHROPIC_API_KEY`. Модель задаётся в `src/Crm.Agent/appsettings.json` (`Anthropic:Model`, по умолчанию `claude-sonnet-5-5`).

Как выглядит работа:

```
Вы> Поставь задачу на завтра: позвонить Наталье Беловой по договору
Claude>
  → search_contacts(query: "Белова")
    ✓ Найдено контактов: 1
  → create_task(title: "Позвонить Наталье Беловой по договору", dueDate: "2026-10-04", contactId: 2)
  Инструмент изменит данные CRM. Выполнить? [y/N] y
    ✓ Создана задача: #7 Позвонить Наталье Беловой по договору · срок 04.10.2026 · …
Готово: задача на завтра, привязана к контакту Наталья Белова.
  [токены: вход 6120, выход 160]
```

Пример ответа модели условный: формулировки и порядок вызовов у живой модели будут свои.

Команды: `/tools` — инструменты сервера, `/prompts` и `/prompt call_prep contactId=1` — промпты сервера, `/reset` — очистить историю, `exit`.

Как устроено (около 350 строк в `src/Crm.Agent`):

- `McpClient` подключается к серверу, `ListToolsAsync()` отдаёт инструменты как `AIFunction`. Их можно передать любому `IChatClient` — Claude, OpenAI, локальной модели.
- `AnthropicClient.AsIChatClient(model)` — официальный SDK Claude под абстракцией Microsoft.Extensions.AI. `UseFunctionInvocation()` сам гоняет цикл «модель просит инструмент → вызов → результат обратно в модель».
- `ToolApproval` перехватывает каждый вызов. Читающие инструменты выполняются сразу. Изменяющие ждут `y` от человека, а решение принимается по аннотациям `readOnlyHint` / `destructiveHint`, которые сервер отдаёт в `tools/list`.
- Инструкции сервера (`ServerInstructions`) входят в системный промпт, туда же добавляется сегодняшняя дата, чтобы модель понимала «завтра».
- После каждого ответа выводится расход токенов — сразу видно, сколько стоит диалог.

## GraphQL API

Третий адаптер над `Crm.Core` — GraphQL на [Hot Chocolate 16](https://chillicream.com/docs/hotchocolate), проект `src/Crm.GraphQL`. Чтение идёт через EF Core напрямую: фильтры, сортировка и пагинация транслируются в SQL. Мутации вызывают те же сервисы, что REST и MCP, — с той же валидацией, правами и записью в историю.

| Адрес | Что там |
|---|---|
| `POST /graphql` | Запросы и мутации; подписки через SSE (`Accept: text/event-stream`) |
| `/graphql/ws` | Подписки по WebSocket, протокол graphql-ws |
| `GET /graphql/schema.graphql` | Схема в SDL — для генерации клиентов (только Development) |
| `/graphql/ui` | IDE Nitro (только Development) |

Всё, кроме IDE, — только с API-ключом, как `/api` и `/mcp`. Открой http://localhost:5080/graphql/ui и нажми **Create Document**: адрес `/graphql` уже подставлен. На вкладке **HTTP Headers** добавь заголовок `Authorization` со значением `Bearer anna-dev-key` и нажми ⟳ (Reload Schema) — появятся схема, автодополнение и описания полей. IDE раздаётся из сборки, а не с CDN, поэтому работает без интернета.

Интроспекция, схема в SDL и IDE доступны только в Development; `dotnet run` запускает профиль с `ASPNETCORE_ENVIRONMENT=Development`.

Проверка из консоли:

```bash
curl http://localhost:5080/graphql -H "Authorization: Bearer anna-dev-key" -H "Content-Type: application/json" \
  -d '{"query":"{ me { fullName role } pipeline { openCount weightedForecast } }"}'
```

Сделки на стадиях КП и переговоров с ответственным, контактом и историей — одним запросом:

```graphql
query {
  deals(
    first: 10
    where: { stage: { in: [PROPOSAL, NEGOTIATION] } }
    order: [{ amount: DESC }]
  ) {
    totalCount
    nodes {
      title
      amount
      stage
      weightedAmount
      owner { fullName }
      contact { fullName company { name } }
      activities { type subject occurredAt }
    }
    pageInfo { hasNextPage endCursor }
  }
}
```

Бизнес-ошибка мутации приходит как данные в `errors`, а не как исключение. Без причины перевод в `LOST` не пройдёт:

```graphql
mutation {
  moveDealStage(input: { dealId: 1, stage: LOST }) {
    deal { stage }
    errors {
      __typename
      ... on Error { message }
    }
  }
}
```

Ответ: `errors: [{ "__typename": "ValidationError", "message": "Для перевода в Lost укажи причину проигрыша (lostReason)." }]`.

Подписка — открой её в одной вкладке Nitro, а в другой переведи сделку `moveDealStage`. Браузер не умеет передавать заголовки при открытии WebSocket, поэтому Nitro сама переключится на SSE, и ключ из HTTP Headers сработает:

```graphql
subscription {
  onDealStageChanged {
    previousStage
    stage
    changedBy { fullName }
    deal { title amount }
  }
}
```

Больше примеров, в том числе пагинация по курсору, проверка прав и подписка через SSE в curl, — в `src/Crm.Api/Crm.Api.http`.

| Операция | Что делает |
|---|---|
| `me`, `users` | Текущий пользователь, все пользователи |
| `deals`, `contacts`, `companies` | Списки с `where`, `order`, курсорной пагинацией и `totalCount` |
| `deal(id)`, `contact(id)`, `company(id)`, `task(id)` | Сущность по ID |
| `searchContacts(query)` | Поиск контактов без учёта регистра — тот же, что у REST и MCP |
| `pipeline`, `staleDeals`, `myTasks`, `dealStages` | Сводка по воронке, зависшие сделки, мои задачи, справочник стадий |
| `auditLog`, `staleDealsClosurePreview` | Аудит вызовов агента, превью массового закрытия — **только Admin** |
| `createContact`, `createDeal`, `updateDeal`, `moveDealStage` | Контакты и сделки |
| `logActivity`, `createTask`, `updateTask`, `completeTask` | История и задачи |
| `closeStaleDeals` | Массовое закрытие по токену из превью — **только Admin** |
| `onDealStageChanged` | Подписка на смену стадии сделки |

Как устроено:

- **Граф, а не DTO.** Типы схемы — сущности домена (`Deal`, `Contact`, `Company`, `User`, `Activity`, `Task`) со списком полей, заданным явно: навигационные свойства EF наружу не выходят, а хэш API-ключа не попадёт в схему, даже если в сущность добавят поля. Связи (`deal.owner`, `contact.deals` и т. д.) — отдельные резолверы.
- **DataLoader против N+1.** Связи грузятся через DataLoader, которые генерирует source generator: 20 сделок с ответственными и контактами — это 3 SQL-запроса, а не 41. Кэш живёт в пределах запроса. Поэтому `Deal` одинаково работает в списке, в `deal(id:)`, в payload мутации и в событии подписки.
- **Фильтрация, сортировка, пагинация.** `deals`, `contacts`, `companies`, `auditLog` возвращают `IQueryable`, и Hot Chocolate собирает из `where`, `order`, `first`/`after` один SQL-запрос. Поля фильтров и сортировки — белый список: иначе через `where: { owner: { apiKeyHash: … } }` можно было бы перебирать хэш ключа. К сортировке клиента добавляется уникальный ключ, поэтому страницы не «прыгают» при равных значениях.
- **Мутации через сервисы.** Mutation conventions генерируют `input` и `payload`, а бизнес-ошибки сервисов становятся типами в `payload.errors`: `ValidationError | NotFoundError | ForbiddenError | ConflictError`, у каждой мутации — только свои. Каждая мутация получает отдельный DI-scope и `DbContext`: если одна мутация в запросе упала, её незавершённые изменения не сохранит следующая.
- **Ошибки запросов.** `CrmErrorFilter` отдаёт текст бизнес-ошибки и код (`NOT_FOUND`, `FORBIDDEN`, `CONFLICT`, `VALIDATION_FAILED`) вместо обезличенного «Unexpected Execution Error». Неожиданные исключения по-прежнему скрыты.
- **Авторизация.** Весь `/graphql` — только с API-ключом. Поверх этого `[Authorize(Roles = Admin)]` на отдельных полях: менеджер получит `AUTH_NOT_AUTHORIZED` по `auditLog`, а остальные поля того же запроса выполнятся. Права на конкретные сделки и задачи, как и раньше, проверяют сервисы.
- **Защита от тяжёлых запросов.** Cost analysis (в Hot Chocolate 16 включён по умолчанию, лимит — 1000): связи через DataLoader стоят 10 за элемент, а у вложенных списков задан ожидаемый размер (`[ListSize]`), поэтому запрос «50 контактов → сделки → история → автор» отклоняется до выполнения. Ещё лимиты: глубина запроса — 12, циклы вида `deal → contact → deals → contact`, страница — до 50 элементов. Стоимость любого запроса покажет заголовок `GraphQL-Cost: report`; если легитимные запросы не проходят, лимиты меняются в `ModifyCostOptions`.
- **Подписки.** `moveDealStage` и `closeStaleDeals` публикуют `onDealStageChanged` через `ITopicEventSender`. В событии только идентификаторы, а сделку подписчик получает через DataLoader. Поэтому in-memory провайдер можно заменить на Redis или Postgres для нескольких экземпляров сервера без изменений кода.

## Что умеет MCP-сервер

**Инструменты** (16). Аннотации `readOnly` / `destructive` / `idempotent` помогают клиенту решать, где спрашивать подтверждение.

| Инструмент | Что делает | Тип |
|---|---|---|
| `search_contacts` | Поиск по имени, email, телефону, компании | чтение |
| `get_contact_card` | Карточка: компания, открытые сделки, история, задачи | чтение |
| `create_contact` | Новый контакт; компания находится или создаётся по названию | запись |
| `list_deals` | Сделки по стадии, ответственному (имя, фамилия или email), «только мои», поиск | чтение |
| `get_deal_card` | Карточка сделки с историей и задачами | чтение |
| `create_deal` | Новая сделка; компания подставляется из контакта | запись |
| `update_deal` | Изменить название, сумму или дату закрытия открытой сделки; изменения пишутся в историю | запись |
| `move_deal_stage` | Смена стадии, автоматически пишется в историю; для Lost нужна причина | запись |
| `get_pipeline_summary` | Воронка по стадиям, взвешенный прогноз, зависшие сделки | чтение |
| `list_stale_deals` | Какие именно сделки зависли и сколько дней без движения; доступно всем | чтение |
| `log_activity` | Звонок, письмо, встреча или заметка | запись |
| `create_task` | Следующий шаг со сроком | запись |
| `update_task` | Перенести срок, изменить название или подробности невыполненной задачи | запись |
| `list_my_tasks` | Мои задачи: Open, Overdue, Today, Week | чтение |
| `complete_task` | Закрыть задачу, результат пишется в историю | запись |
| `close_stale_deals` | Массово закрыть зависшие сделки, **только Admin, в два шага** | опасная |

**Ресурсы**: `crm://reference/deal-stages` (справочник стадий), `crm://contacts/{contactId}`, `crm://deals/{dealId}`.

**Промпты**: `call_prep` (подготовка к звонку по контакту), `pipeline_review` (разбор воронки).

Что попросить у агента для проверки:

- «Что у меня на сегодня и что просрочено?»
- «Подготовь меня к звонку с Иваном Петровым»
- «Позвонила Наталье Беловой: договор на сервис согласован. Переведи сделку в Won, запиши звонок и поставь задачу выставить счёт завтра»
- «Найди зависшие сделки и закрой их» — агент покажет превью и попросит подтверждение

## Как это устроено

- **Права.** Агент всегда действует от имени конкретного пользователя: в HTTP — того, чей API-ключ пришёл в запросе, в stdio — из конфигурации. Менеджер меняет только свои сделки и задачи, администратор — любые. Проверки живут в сервисах (`Crm.Core`), поэтому одинаково работают для REST, HTTP и stdio.
- **`[Authorize]` на инструментах.** `close_stale_deals` помечен `[Authorize(Roles = "Admin")]`. В HTTP-хосте `.AddAuthorizationFilters()` убирает его из `tools/list` у менеджера, а прямой вызов отклоняется. В stdio HTTP-авторизации нет, там срабатывает проверка в сервисе.
- **Ошибки для модели.** Бизнес-ошибки (`CrmException`) фильтр превращает в результат с `isError: true` и понятным текстом: «Для перевода в Lost укажи причину». По умолчанию SDK прячет текст исключений, и модель не поняла бы, что исправить. Неожиданные исключения уходят в лог, модели — обезличенное сообщение.
- **Аудит.** Каждый вызов инструмента пишется в таблицу `AuditLog`: пользователь, канал (`mcp-http` / `mcp-stdio`), инструмент, аргументы (`jsonb`), успех или ошибка, длительность. Запись идёт в отдельном `DbContext`, чтобы упавшая операция не «доехала» до БД вместе с аудитом.
- **Двухшаговые массовые операции.** `close_stale_deals` без токена только показывает список и выдаёт токен — хэш набора сделок. Изменения применяются при повторном вызове с этим токеном. Если набор успел измениться, токен не подойдёт: агент не может закрыть не то, что видел пользователь.
- **Схемы как защита.** В схемах только допустимые значения: `create_deal` принимает лишь открытые стадии, `log_activity` — только типы, которые можно создавать вручную.
- **Ответы — компактный Markdown** с явными `#id`, а не сырой JSON: модели проще, токенов меньше.
- **Stateless HTTP.** Каждый POST на `/mcp` обрабатывается независимо, без `Mcp-Session-Id`, так что сервер можно ставить за балансировщик. В SDK 2.x это режим по умолчанию и родной для ревизии протокола 2026-07-28; старые клиенты с `initialize` тоже работают. Сессии понадобятся только для серверных уведомлений и подписок на ресурсы.

## Как добавить инструмент

1. Метод в сервисе `Crm.Core` — с валидацией и проверкой прав. При ошибке бросай `CrmValidationException` / `CrmNotFoundException` / `CrmForbiddenException`.
2. Метод с `[McpServerTool]` в классе из `src/Crm.Mcp/Tools`: принять аргументы, вызвать сервис, отформатировать ответ.
3. Для нового класса добавь `.WithTools<T>()` в `CrmMcpServerBuilderExtensions`.

`Description` пиши как документацию для коллеги: модель выбирает инструмент только по ней. Для фиксированных значений бери enum — они попадут в схему списком.

## Как добавить поле или мутацию в GraphQL

1. Логика — в сервисе `Crm.Core`, как и для MCP.
2. Запрос — статический метод в классе с `[QueryType]`, мутация — с `[MutationType]` в `src/Crm.GraphQL/<раздел>`. Регистрировать ничего не нужно: source generator добавит поле в `AddCrmTypes()`.
3. Новая связь сущности — метод с `[Parent]` в её классе `…Node` и DataLoader в `CrmDataLoaders`, иначе получится N+1.
4. Ошибки мутации, которые клиент должен разбирать, перечисли в `[Error<…>]` — они появятся в `payload.errors`.

## Тесты

`tests/Crm.GraphQL.Tests` — интеграционные тесты GraphQL API на xUnit v3. `WebApplicationFactory` поднимает весь Crm.Api в памяти: аутентификацию по API-ключу, `/graphql`, сервисы и настоящий PostgreSQL. In-memory провайдер EF не подходит: сервисы используют `ILIKE` и `jsonb`. Каждый тестовый класс создаёт свою временную базу `crm_tests_<guid>` с демо-данными и удаляет её после прогона, поэтому классы выполняются параллельно.

```bash
dotnet test
```

По умолчанию тесты подключаются к тому же серверу, что и приложение (`localhost:5433`, `postgres` / `12345`). Другой сервер задаёт переменная окружения `CRM_TESTS_POSTGRES`, например для `docker compose`: `CRM_TESTS_POSTGRES=Host=localhost;Port=5432;Username=crm;Password=crm`.

Что проверяют:

- **Контракт схемы.** `SchemaTests` сравнивает SDL с эталоном `Snapshots/schema.graphql`. Любое изменение схемы роняет тест и видно в diff эталона на ревью. Если изменение ожидаемое и не ломает клиентов, замени эталон полученным файлом `schema.received.graphql`.
- **Запросы.** Фильтрация, сортировка и курсорная пагинация, фильтр по связанным сущностям, поиск, `null` для несуществующего ID, `AUTH_NOT_AUTHORIZED` у менеджера на поле администратора при выполнении остального запроса, бизнес-ошибка с текстом и кодом, отказ cost analysis до обращения к БД.
- **N+1.** Перехватчик EF Core считает SQL-запросы: 8 сделок с ответственным, контактом и компанией — 5 запросов (проверка ключа, страница сделок, пользователи, контакты, компании) вместо 25 с лишним.
- **Мутации.** Ошибки сервисов приходят типами в `payload.errors` (`ValidationError`, `ForbiddenError`, `NotFoundError`, `ConflictError`), смена стадии пишется в историю, задача создаётся и закрывается один раз, массовое закрытие — в два шага и только у администратора.
- **Подписки.** Событие `onDealStageChanged` доходит до подписчика по WebSocket (`graphql-transport-ws`), а без ключа соединение не открывается.

## Миграции

Для быстрого старта схема создаётся через `EnsureCreated`. Чтобы перейти на миграции:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/Crm.Core --startup-project src/Crm.Api
docker compose down -v && docker compose up -d   # пересоздать БД: схему от EnsureCreated миграции не примут
```

При следующем запуске `CrmDatabase` увидит миграции и вызовет `MigrateAsync`.

## Перед продакшном

- **Аутентификация.** API-ключи подходят для разработки и внутренних интеграций. Для удалённого сервера с внешними клиентами используй OAuth по спецификации MCP: `AddJwtBearer` + `.AddMcp(...)` из `ModelContextProtocol.AspNetCore` публикует Protected Resource Metadata, и клиент сам пройдёт авторизацию у твоего провайдера (Keycloak, Entra ID и т. п.). Готовый пример — `samples/ProtectedMcpServer` в репозитории SDK.
- **Хосты.** `AllowedHosts` сейчас ограничен localhost — это защита от DNS rebinding. В продакшне укажи точные имена хостов. CORS не включай без необходимости.
- **Секреты.** Строку подключения и ключи — в user-secrets или переменные окружения, не в `appsettings.json`.
- **Схема БД.** Перейди на миграции (см. выше) и отключи `Crm:SeedDemoData`.
- **Нагрузка.** Добавь rate limiting на `/mcp`: агент может вызывать инструменты очень часто.
- **GraphQL.** Для публичных клиентов разреши только заранее сохранённые запросы (trusted documents) и подбери лимиты стоимости по реальным запросам. Интроспекция, схема в SDL и IDE Nitro вне Development уже выключены.

## Куда развивать

- Интеграционные тесты MCP: `McpClient` против поднятого сервера, как уже сделано для GraphQL. Testcontainers вместо локального PostgreSQL, чтобы тесты шли в CI без подготовки.
- Семантический поиск по истории общения (pgvector).
- Подтверждения через elicitation, если клиент их поддерживает.
- Нативные подтверждения в агенте: `ApprovalRequiredAIFunction` из Microsoft.Extensions.AI вместо консольного вопроса — пригодится для веб-интерфейса.
- Сжатие истории диалога (`UseChatReducer`) для длинных сессий агента.
- GraphQL: в CI отличать ломающие изменения схемы от безопасных (сейчас snapshot-тест останавливает на любом), пагинация вложенных списков (`contact.activities`) через DataLoader и `PagingArguments`, аудит GraphQL-мутаций, как у MCP-инструментов.
