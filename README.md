# AI-проекты на .NET: MCP-серверы и LLM-агенты

Пет-проекты Романа Зинченко, Senior .NET-разработчика. Тема — как встраивать LLM в бизнес-системы так, чтобы агенту можно было доверять: MCP-серверы, агенты на Claude API, защита от ошибок модели и prompt injection.

| Проект | Что это | Главное |
|---|---|---|
| [SqlAnalyst](SqlAnalyst) | Агент отвечает на вопросы к PostgreSQL на естественном языке и строит графики | Read-only MCP-сервер и эшелонированная защита: валидатор SQL, проверка плана запроса, права роли, canary-метки, детектор prompt injection. Тесты на корпусе атак |
| [CrmMcp](CrmMcp) | CRM, с которой работают и люди, и AI-агент | MCP-сервер на 16 инструментов (Streamable HTTP и stdio), REST и GraphQL поверх общей бизнес-логики. Свой агент на Claude API: изменяющие действия — только после подтверждения человеком |
| [RoslynReview](RoslynReview) | MCP-сервер, который даёт AI-агенту точную, на уровне компилятора, навигацию по C#-решению для код-ревью | Изменённые символы, исходники, места вызова; все инструменты только читают. В работе: агент ревью и evals |
| [RoslynReviewApi](RoslynReviewApi) | HTTP-API на FastAPI поверх RoslynReview | REST с OpenAPI-схемой для CI и веб-интерфейсов; весь контекст для ревью изменения одним запросом |

## Общие принципы

- **MCP — тонкий слой над бизнес-логикой.** В CrmMcp инструменты вызывают те же сервисы, что REST и GraphQL, поэтому валидация, права и аудит одинаковы для человека и агента.
- **Агенту — минимум прав.** Read-only инструменты и роль в БД, аннотации `readOnlyHint` / `destructiveHint`, подтверждение человеком для всего, что меняет данные.
- **Ни один рубеж не единственный.** В SqlAnalyst запрос проходит валидатор, проверку плана, read-only транзакцию и права роли; данные из базы считаются недоверенными и проходят детектор prompt injection.
- **Тесты на реальной инфраструктуре.** Интеграционные и end-to-end тесты с PostgreSQL и настоящим MCP-сервером.

## Стек

.NET 10, ASP.NET Core, [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), Microsoft.Extensions.AI, [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp) (Claude API), Roslyn, EF Core, Npgsql, PostgreSQL, Hot Chocolate, xUnit v3, Docker Compose, GitHub Actions.

RoslynReviewApi — Python 3.11, FastAPI, MCP Python SDK, pytest.

## Запуск

У каждого проекта свой README с быстрым стартом. Обычно нужны .NET 10 SDK и Docker, для агентов — ключ Claude API.

## Как сделано

Проекты разработаны вместе с AI-агентом Claude Code: я ставил задачи и проверял результат, часть кода написал агент (такие коммиты подписаны автором Claude).

## Контакты

Telegram: [@z_roman_work](https://t.me/z_roman_work)
