using Crm.Core.Data;
using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Filtering;
using Crm.GraphQL.Infrastructure;
using GreenDonut;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Data;
using HotChocolate.Data.Sorting;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace Crm.GraphQL.Contacts;

[QueryType]
public static partial class ContactQueries
{
    [GraphQLDescription("Контакты с фильтрацией, сортировкой и курсорной пагинацией. Без order — по фамилии и имени.")]
    [UsePaging]
    [UseFiltering(typeof(ContactFilterInputType))]
    [UseSorting(typeof(ContactSortInputType))]
    public static IQueryable<Contact> GetContacts(CrmDbContext db, ISortingContext sorting)
    {
        sorting.UseDefaultOrder<Contact, int>(q => q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName), c => c.Id);
        return db.Contacts.AsNoTracking();
    }

    [GraphQLDescription("Контакт по ID.")]
    public static async Task<Contact?> GetContactAsync(
        int id,
        IContactByIdDataLoader contactById,
        CancellationToken cancellationToken)
        => await contactById.LoadAsync(id, cancellationToken);

    // Поиск — тот же метод сервиса, что у REST (/api/contacts?q=) и MCP (search_contacts): без учёта регистра,
    // по имени, фамилии, email, телефону и компании. Сервис отдаёт плоские DTO, а клиенту GraphQL нужен граф,
    // поэтому сущности догружаются одним запросом через DataLoader.
    [GraphQLDescription("Поиск контактов по имени, email, телефону или названию компании без учёта регистра.")]
    [ListSize(AssumedSize = 50, SlicingArguments = ["limit"], RequireOneSlicingArgument = false)]
    public static async Task<IReadOnlyList<Contact>> SearchContactsAsync(
        [GraphQLDescription("Строка поиска, например «петров» или «vector»")] string query,
        ContactService contacts,
        IContactByIdDataLoader contactById,
        CancellationToken cancellationToken,
        [GraphQLDescription("Сколько контактов вернуть, от 1 до 50")] int limit = 10)
    {
        var found = await contacts.SearchAsync(query, limit, cancellationToken);
        return await contactById.LoadRequiredAsync(found.Select(c => c.Id).ToArray(), cancellationToken);
    }
}
