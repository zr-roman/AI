using Crm.Core.Data;
using Crm.Core.Domain;
using Crm.GraphQL.DataLoaders;
using Crm.GraphQL.Filtering;
using Crm.GraphQL.Infrastructure;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Sorting;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace Crm.GraphQL.Companies;

[QueryType]
public static partial class CompanyQueries
{
    // Резолвер возвращает IQueryable, а where, order и first/after Hot Chocolate превращает
    // в один SQL-запрос: WHERE … ORDER BY … LIMIT. Порядок атрибутов важен: пагинация снаружи.
    [GraphQLDescription("Компании с фильтрацией, сортировкой и курсорной пагинацией. Без order — по названию.")]
    [UsePaging]
    [UseFiltering(typeof(CompanyFilterInputType))]
    [UseSorting(typeof(CompanySortInputType))]
    public static IQueryable<Company> GetCompanies(CrmDbContext db, ISortingContext sorting)
    {
        sorting.UseDefaultOrder<Company, int>(q => q.OrderBy(c => c.Name), c => c.Id);
        return db.Companies.AsNoTracking();
    }

    [GraphQLDescription("Компания по ID.")]
    public static async Task<Company?> GetCompanyAsync(
        int id,
        ICompanyByIdDataLoader companyById,
        CancellationToken cancellationToken)
        => await companyById.LoadAsync(id, cancellationToken);
}
