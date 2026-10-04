using System.ComponentModel;
using Crm.Core.Services;
using Crm.Mcp.Formatting;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Crm.Mcp.Prompts;

// Промпты — готовые сценарии, которые пользователь сам выбирает в MCP-клиенте.
// Сервер подставляет в них актуальные данные из CRM.
[McpServerPromptType]
public sealed class CrmPrompts(ContactService contacts, DealService deals, TaskService tasks, CrmFormatter format)
{
    [McpServerPrompt(Name = "call_prep", Title = "Подготовка к звонку")]
    [Description("Собирает карточку клиента и просит подготовить план звонка.")]
    public async Task<ChatMessage> CallPrep(
        [Description("ID контакта")] int contactId,
        CancellationToken cancellationToken = default)
    {
        var card = format.ContactCard(await contacts.GetCardAsync(contactId, cancellationToken));

        return new ChatMessage(ChatRole.User, $"""
            Подготовь меня к звонку с клиентом. Ниже актуальная карточка из CRM.

            {card}

            Нужно:
            1. Резюме отношений с клиентом в 2–3 предложениях.
            2. Цель звонка и 3 вопроса, которые стоит задать.
            3. Риски и возможные возражения по открытым сделкам.
            4. Что зафиксировать после звонка: предложи log_activity и create_task с конкретными формулировками.
            """);
    }

    [McpServerPrompt(Name = "pipeline_review", Title = "Разбор воронки")]
    [Description("Собирает сводку по воронке, крупные открытые сделки и мои просроченные задачи и просит провести разбор.")]
    public async Task<ChatMessage> PipelineReview(CancellationToken cancellationToken = default)
    {
        var summary = format.Pipeline(await deals.GetPipelineSummaryAsync(onlyMine: false, cancellationToken), onlyMine: false);
        var filter = new DealFilter();
        var topDeals = format.DealList(await deals.ListAsync(filter, limit: 15, cancellationToken), filter);
        var overdue = format.TaskList(await tasks.ListMineAsync(TaskFilter.Overdue, cancellationToken), TaskFilter.Overdue);

        return new ChatMessage(ChatRole.User, $"""
            Проведи разбор воронки продаж на основе данных CRM.

            {summary}

            {topDeals}

            {overdue}

            Нужно:
            1. Главные выводы о состоянии воронки — коротко, без пересказа таблиц.
            2. Сделки, которым нужно внимание в первую очередь, и почему.
            3. Конкретные следующие шаги по каждой из них.
            Ничего не меняй в CRM без моего подтверждения: сначала предложи действия списком.
            """);
    }
}
