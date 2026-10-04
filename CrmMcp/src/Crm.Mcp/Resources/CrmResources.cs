using System.ComponentModel;
using Crm.Core.Services;
using Crm.Mcp.Formatting;
using ModelContextProtocol.Server;

namespace Crm.Mcp.Resources;

// Ресурсы — данные только для чтения, которые клиент (или пользователь) может подложить в контекст сам,
// без вызова инструмента моделью.
[McpServerResourceType]
public sealed class CrmResources(ContactService contacts, DealService deals, CrmFormatter format)
{
    [McpServerResource(UriTemplate = "crm://reference/deal-stages", Name = "deal_stages", Title = "Стадии воронки", MimeType = "text/markdown")]
    [Description("Справочник стадий воронки: значения, вероятности и правила переходов.")]
    public static string DealStages() => CrmFormatter.DealStagesReference();

    [McpServerResource(UriTemplate = "crm://contacts/{contactId}", Name = "contact", Title = "Карточка контакта", MimeType = "text/markdown")]
    [Description("Карточка контакта со сделками, историей и задачами.")]
    public async Task<string> Contact(int contactId, CancellationToken cancellationToken)
    {
        var card = await contacts.GetCardAsync(contactId, cancellationToken);
        return format.ContactCard(card);
    }

    [McpServerResource(UriTemplate = "crm://deals/{dealId}", Name = "deal", Title = "Карточка сделки", MimeType = "text/markdown")]
    [Description("Карточка сделки с историей и задачами.")]
    public async Task<string> Deal(int dealId, CancellationToken cancellationToken)
    {
        var card = await deals.GetCardAsync(dealId, cancellationToken);
        return format.DealCard(card);
    }
}
