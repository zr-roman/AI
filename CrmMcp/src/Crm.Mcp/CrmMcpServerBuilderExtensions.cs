using Crm.Mcp.Formatting;
using Crm.Mcp.Prompts;
using Crm.Mcp.Resources;
using Crm.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Crm.Mcp;

public static class CrmMcpServerBuilderExtensions
{
    /// <summary>
    /// Регистрирует MCP-сервер CRM: инструменты, ресурсы, промпты и фильтры.
    /// Транспорт (HTTP или stdio) добавляет хост.
    /// </summary>
    /// <param name="auditChannel">Метка канала в аудите, например "mcp-http" или "mcp-stdio".</param>
    public static IMcpServerBuilder AddCrmMcpServer(this IServiceCollection services, string auditChannel)
    {
        services.AddSingleton<CrmFormatter>();

        return services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "crm",
                    Title = "CRM",
                    Version = typeof(CrmMcpServerBuilderExtensions).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                };
                options.ServerInstructions = ServerInstructions;
            })
            .WithTools<ContactTools>()
            .WithTools<DealTools>()
            .WithTools<ActivityTools>()
            .WithTools<TaskTools>()
            .WithResources<CrmResources>()
            .WithPrompts<CrmPrompts>()
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(CrmMcpFilters.CallTool(auditChannel));
                filters.AddReadResourceFilter(CrmMcpFilters.ReadResource());
                filters.AddGetPromptFilter(CrmMcpFilters.GetPrompt());
            });
    }

    // Инструкции попадают в контекст модели при подключении к серверу: здесь — правила работы с CRM,
    // которые не привязаны к одному инструменту.
    private const string ServerInstructions = """
        CRM отдела продаж: контакты, компании, сделки, история активностей и задачи.
        Правила:
        - Идентификаторы (#12) бери только из ответов инструментов, не придумывай.
        - Перед созданием контакта ищи дубликаты через search_contacts.
        - Суммы — в рублях, даты — в формате YYYY-MM-DD.
        - Стадии воронки: Lead → Qualified → Proposal → Negotiation → Won или Lost. Для Lost нужна причина.
        - После звонка или встречи фиксируй итог через log_activity, следующий шаг — через create_task.
        - Массовые изменения (close_stale_deals) — только в два шага: превью, затем подтверждение токеном
          и только после явного согласия пользователя.
        - Если инструмент вернул ошибку прав доступа, не пытайся обойти её другими инструментами — сообщи пользователю.
        """;
}
