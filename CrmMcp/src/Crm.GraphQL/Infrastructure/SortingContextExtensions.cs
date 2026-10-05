using System.Linq.Expressions;
using HotChocolate.Data.Sorting;

namespace Crm.GraphQL.Infrastructure;

internal static class SortingContextExtensions
{
    /// <summary>
    /// Порядок по умолчанию и стабильная курсорная пагинация.
    /// Если клиент передал order, к его сортировке добавляется уникальный ключ (ThenBy по id):
    /// без него при равных значениях (одинаковая сумма, одна компания) строки «прыгали» бы между страницами.
    /// Если order нет — применяется сортировка по умолчанию. Задать её прямо в резолвере через OrderBy нельзя:
    /// Hot Chocolate увидит готовый OrderBy и добавит сортировку клиента только как ThenBy.
    /// </summary>
    public static void UseDefaultOrder<T, TKey>(
        this ISortingContext sorting,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrder,
        Expression<Func<T, TKey>> uniqueKey)
        => sorting.OnAfterSortingApplied<IQueryable<T>>((sortedByClient, query) =>
            sortedByClient
                ? ((IOrderedQueryable<T>)query).ThenBy(uniqueKey)
                : defaultOrder(query).ThenBy(uniqueKey));
}
