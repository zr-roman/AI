# RoslynReview API

HTTP-API на FastAPI для [RoslynReview](../RoslynReview) — MCP-сервера, который даёт агенту код-ревью
точную, на уровне компилятора, навигацию по C#-решению.

MCP удобен для Claude Desktop и Claude Code, но не для всего остального: CI-джобы, веб-интерфейса, ревьюера на
другом LLM-фреймворке или обычного `curl`. Этот сервис запускает .NET-сервер один раз, держит с ним MCP-сессию по
stdio и отдаёт его инструменты как REST-эндпоинты с OpenAPI-схемой. Плюс один эндпоинт, которого в MCP нет:
`/v1/review-context` собирает весь контекст для ревью изменения за один запрос.

```
HTTP-клиент ──REST──► FastAPI ──MCP/stdio──► RoslynReview.McpServer (.NET, Roslyn) ──► ваше решение .slnx
```

## Эндпоинты

| Метод | Путь | Что делает | MCP-инструмент |
|-------|------|-----------|----------------|
| `POST` | `/v1/changed-symbols` | Какие члены и типы затрагивает изменение (`baseRef` или `diff`) | `get_changed_symbols` |
| `GET` | `/v1/symbols/source?id=…&maxLines=150` | Исходник символа с номерами строк (`text/plain`) | `get_symbol_source` |
| `GET` | `/v1/symbols/callers?id=…&maxResults=50` | Места вызова, сгруппированные по вызывающему члену | `find_callers` |
| `POST` | `/v1/review-context` | Всё сразу: изменённые символы, их код и вызывающие | все три |
| `GET` | `/health` | `ok`, когда MCP-сервер подключён; иначе 503 | — |

Интерактивная документация — `http://127.0.0.1:8000/docs`.

`/v1/review-context` повторяет порядок работы, который сервер советует агенту: сначала `get_changed_symbols`,
затем исходник каждого изменённого символа и вызывающие для каждого **изменённого не-private члена** — именно их
изменение может сломать чужой код. У добавленных членов вызывающих ещё нет, а все использования целого типа —
слишком много шума. Запросы к серверу идут параллельно (не больше `ROSLYN_REVIEW_MAX_CONCURRENCY`). Если по одному
символу инструмент вернул ошибку, она попадает в `errors` этого символа, а не роняет весь ответ.

```bash
curl -s localhost:8000/v1/review-context -H 'Content-Type: application/json' \
  -d '{"baseRef": "main", "maxSymbols": 20, "maxLines": 80}'
```

```json
{
  "changes": { "symbols": [ ... ], "files": [ ... ] },
  "symbols": [
    {
      "symbol": { "id": "M:SampleShop.Orders.OrderService.PlaceOrderAsync(...)", "change": "modified", ... },
      "source": "14 |     public async Task<OrderResult> PlaceOrderAsync(Order order, CancellationToken cancellationToken)\n...",
      "callers": { "totalSites": 2, "truncated": false, "callers": [ ... ] },
      "errors": []
    }
  ],
  "skippedSymbols": 0
}
```

PR-дифф можно передать как есть:

```bash
jq -Rs '{diff: .}' pr.diff | curl -s localhost:8000/v1/changed-symbols -H 'Content-Type: application/json' -d @-
```

### Ошибки

Тело ошибки всегда `{"detail": "..."}`. Сообщения инструментов сервер пишет так, чтобы по ним можно было
исправить запрос, поэтому они передаются как есть.

| Код | Когда |
|-----|-------|
| 401 | Задан `ROSLYN_REVIEW_API_KEY`, а заголовка `X-API-Key` нет или он неверный |
| 404 | Символа с таким id нет в решении |
| 422 | Запрос не прошёл валидацию или инструмент его отклонил (например, неизвестная ветка в `baseRef`) |
| 502 | Сервер ответил ошибкой протокола MCP |
| 503 | MCP-сервер не запущен или упал; сервис перезапускает его сам с экспоненциальной задержкой |
| 504 | Вызов инструмента не уложился в `ROSLYN_REVIEW_TOOL_TIMEOUT` |

## Запуск

Нужны Python 3.11+, [uv](https://docs.astral.sh/uv/), .NET 10 SDK и git.

```bash
# 1. Собрать MCP-сервер и восстановить решение, которое будем ревьюить
dotnet build ../RoslynReview/src/RoslynReview.McpServer
dotnet restore path/to/YourApp.slnx

# 2. Запустить API
uv sync
export ROSLYN_REVIEW_SERVER=../RoslynReview/src/RoslynReview.McpServer/bin/Debug/net10.0/RoslynReview.McpServer.dll
export ROSLYN_REVIEW_SOLUTION=path/to/YourApp.slnx
uv run roslyn-review-api --port 8000
```

| Переменная | По умолчанию | |
|------------|--------------|---|
| `ROSLYN_REVIEW_SERVER` | обязательна | Путь к `RoslynReview.McpServer.dll` (запускается через `dotnet`) или к исполняемому файлу |
| `ROSLYN_REVIEW_SOLUTION` | обязательна | `.slnx`, `.sln` или `.csproj` |
| `ROSLYN_REVIEW_REPO_ROOT` | ближайшая папка с `.git` над решением | Корень репозитория для `git diff` |
| `ROSLYN_REVIEW_API_KEY` | нет | Если задан, `/v1/*` требует заголовок `X-API-Key` |
| `ROSLYN_REVIEW_TOOL_TIMEOUT` | `300` | Секунд на один вызов инструмента. Первый вызов может ждать, пока MSBuild загрузит решение |
| `ROSLYN_REVIEW_MAX_CONCURRENCY` | `4` | Параллельных вызовов в `/v1/review-context` |

По умолчанию сервис слушает только `127.0.0.1`. Решение фиксируется при старте, ни один эндпоинт не принимает путь
к файлу. Но загрузка решения выполняет MSBuild-логику репозитория, как любая сборка, поэтому направляйте сервис только
на код, которому доверяете.

## Как устроено

```
src/roslyn_review_api/
  server.py     MCP-клиент: один процесс .NET-сервера на всё время жизни, супервизор с перезапуском, ошибки → исключения
  review.py     /v1/review-context: changed symbols → source + callers параллельно, с семафором
  app.py        FastAPI: роуты, API-ключ, маппинг исключений в HTTP-коды
  models.py     Pydantic-модели, повторяющие JSON сервера (camelCase на проводе)
  settings.py   настройки из ROSLYN_REVIEW_*
tests/
  fake_server.py   MCP-сервер на Python с теми же инструментами и текстами ошибок, что у .NET-сервера
  test_api.py      эндпоинты через настоящий stdio-транспорт и fake_server
  test_server.py   таймауты, падение и перезапуск сервера, сервер, который не стартует, API-ключ
  test_e2e.py      те же эндпоинты против настоящего .NET-сервера и фикстуры SampleShop (в CI)
```

- **Одно соединение.** Сервер загружает решение один раз и кэширует его, поэтому процесс на запрос был бы
  медленным. MCP мультиплексирует запросы по id, так что параллельные HTTP-запросы идут через одну сессию.
- **Супервизор.** anyio требует, чтобы соединение закрывала та же задача, что его открыла, поэтому им владеет
  отдельная фоновая задача. Если процесс завершился, текущий запрос получает 503, а задача поднимает сервер заново
  (1 с, 2 с, 4 с… до 30 с).
- **Протокол.** Клиент использует классическое рукопожатие `initialize` (`mode="legacy"`), которое поддерживает и
  .NET SDK.

## Тесты

```bash
uv run pytest                 # юнит-тесты против fake_server; e2e пропускаются
uv run ruff check . && uv run ruff format --check .
```

End-to-end против настоящего сервера:

```bash
dotnet build ../RoslynReview/src/RoslynReview.McpServer
dotnet restore ../RoslynReview/tests/fixtures/SampleShop/SampleShop.slnx
ROSLYN_REVIEW_E2E_SERVER=$PWD/../RoslynReview/src/RoslynReview.McpServer/bin/Debug/net10.0/RoslynReview.McpServer.dll \
  uv run pytest -m e2e
```

CI (`.github/workflows/roslyn-review-api.yml`) гоняет линтер, юнит-тесты и e2e на каждый PR, затрагивающий
`RoslynReviewApi/` или `RoslynReview/`.
