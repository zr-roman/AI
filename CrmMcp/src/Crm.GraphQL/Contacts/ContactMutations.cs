using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Errors;
using GreenDonut;
using HotChocolate;
using HotChocolate.Types;

namespace Crm.GraphQL.Contacts;

// Мутации — тонкий адаптер над сервисами Crm.Core, как REST-эндпоинты и MCP-инструменты:
// валидация, права и запись в историю живут в сервисах и одинаковы для всех трёх API.
[MutationType]
public static partial class ContactMutations
{
    [GraphQLDescription("Создаёт контакт от имени текущего пользователя. Компания находится по названию или создаётся.")]
    [Error<ValidationError>]
    [Error<ConflictError>]
    public static async Task<CreateContactPayload> CreateContactAsync(
        string firstName,
        string lastName,
        ContactService contacts,
        IContactByIdDataLoader contactById,
        CancellationToken cancellationToken,
        string? email = null,
        string? phone = null,
        string? position = null,
        [GraphQLDescription("Название компании: найдётся существующая или будет создана новая")] string? companyName = null)
    {
        var created = await contacts.CreateAsync(
            new NewContact(firstName, lastName, email, phone, position, companyName),
            cancellationToken);

        var contact = await contactById.LoadRequiredAsync(created.Contact.Id, cancellationToken);
        return new CreateContactPayload(contact, created.CompanyCreated);
    }
}

public sealed record CreateContactPayload(Contact Contact, bool CompanyCreated);
