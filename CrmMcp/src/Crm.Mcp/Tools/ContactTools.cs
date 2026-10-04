using System.ComponentModel;
using Crm.Core.Services;
using Crm.Mcp.Formatting;
using ModelContextProtocol.Server;

namespace Crm.Mcp.Tools;

// Инструменты — тонкий адаптер: принять аргументы, вызвать сервис, отформатировать ответ.
// Вся бизнес-логика и проверки прав — в Crm.Core, их же использует REST API.
[McpServerToolType]
public sealed class ContactTools(ContactService contacts, CrmFormatter format)
{
    [McpServerTool(Name = "search_contacts", Title = "Поиск контактов", ReadOnly = true, OpenWorld = false)]
    [Description("Ищет контакты по имени, фамилии, email, телефону или названию компании. Вызывай перед созданием контакта, чтобы не завести дубликат.")]
    public async Task<string> SearchContacts(
        [Description("Что искать: часть имени, email, телефона или названия компании")] string query,
        [Description("Сколько результатов вернуть, от 1 до 50")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var found = await contacts.SearchAsync(query, limit, cancellationToken);
        return format.ContactList(found, query);
    }

    [McpServerTool(Name = "get_contact_card", Title = "Карточка контакта", ReadOnly = true, OpenWorld = false)]
    [Description("Полная карточка контакта: компания, открытые сделки, последние активности и открытые задачи. Используй перед звонком или встречей.")]
    public async Task<string> GetContactCard(
        [Description("ID контакта, например 3 для #3")] int contactId,
        CancellationToken cancellationToken = default)
    {
        var card = await contacts.GetCardAsync(contactId, cancellationToken);
        return format.ContactCard(card);
    }

    [McpServerTool(Name = "create_contact", Title = "Создать контакт", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Создаёт контакт. Если указана компания, привязывает к существующей с таким названием или создаёт новую. Ответственным становится текущий пользователь. Перед вызовом проверь дубликаты через search_contacts.")]
    public async Task<string> CreateContact(
        [Description("Имя")] string firstName,
        [Description("Фамилия")] string lastName,
        [Description("Email")] string? email = null,
        [Description("Телефон в любом формате")] string? phone = null,
        [Description("Должность")] string? position = null,
        [Description("Название компании как оно звучит у клиента, например ООО «Ромашка»")] string? companyName = null,
        CancellationToken cancellationToken = default)
    {
        var created = await contacts.CreateAsync(
            new NewContact(firstName, lastName, email, phone, position, companyName),
            cancellationToken);

        return format.ContactCreated(created);
    }
}
