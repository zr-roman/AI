using Crm.Core.Data;
using Crm.Core.Domain;
using GreenDonut;
using Microsoft.EntityFrameworkCore;

namespace Crm.GraphQL.DataLoaders;

// DataLoader против N+1: связи (deal.owner, contact.deals, …) не грузятся по одной сущности за раз.
// Пока выполняются резолверы одного уровня, DataLoader копит ключи, а затем делает один запрос
// WHERE id = ANY(@ids) на всех. Кэш живёт в пределах одного GraphQL-запроса.
//
// Source generator по каждому методу создаёт интерфейс и класс: GetUserByIdAsync → IUserByIdDataLoader.
// Словарь в ответе — «один к одному», ILookup — «один ко многим» (для ключа без данных — пустой массив).
// DbContext каждый пакет получает в собственном DI-scope, поэтому параллельные загрузки не конфликтуют.
public static class CrmDataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<int, User>> GetUserByIdAsync(
        IReadOnlyList<int> ids,
        CrmDbContext db,
        CancellationToken cancellationToken)
        => await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

    [DataLoader]
    public static async Task<Dictionary<int, Company>> GetCompanyByIdAsync(
        IReadOnlyList<int> ids,
        CrmDbContext db,
        CancellationToken cancellationToken)
        => await db.Companies.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

    [DataLoader]
    public static async Task<Dictionary<int, Contact>> GetContactByIdAsync(
        IReadOnlyList<int> ids,
        CrmDbContext db,
        CancellationToken cancellationToken)
        => await db.Contacts.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

    [DataLoader]
    public static async Task<Dictionary<int, Deal>> GetDealByIdAsync(
        IReadOnlyList<int> ids,
        CrmDbContext db,
        CancellationToken cancellationToken)
        => await db.Deals.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

    [DataLoader]
    public static async Task<Dictionary<int, Activity>> GetActivityByIdAsync(
        IReadOnlyList<int> ids,
        CrmDbContext db,
        CancellationToken cancellationToken)
        => await db.Activities.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

    [DataLoader]
    public static async Task<Dictionary<int, CrmTask>> GetTaskByIdAsync(
        IReadOnlyList<int> ids,
        CrmDbContext db,
        CancellationToken cancellationToken)
        => await db.Tasks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

    [DataLoader]
    public static async Task<ILookup<int, Contact>> GetContactsByCompanyIdAsync(
        IReadOnlyList<int> companyIds,
        CrmDbContext db,
        CancellationToken cancellationToken)
    {
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.CompanyId != null && companyIds.Contains(c.CompanyId.Value))
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return contacts.ToLookup(c => c.CompanyId!.Value);
    }

    [DataLoader]
    public static async Task<ILookup<int, Deal>> GetDealsByCompanyIdAsync(
        IReadOnlyList<int> companyIds,
        CrmDbContext db,
        CancellationToken cancellationToken)
    {
        var deals = await db.Deals.AsNoTracking()
            .Where(d => d.CompanyId != null && companyIds.Contains(d.CompanyId.Value))
            .OrderByDescending(d => d.Amount).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

        return deals.ToLookup(d => d.CompanyId!.Value);
    }

    [DataLoader]
    public static async Task<ILookup<int, Deal>> GetDealsByContactIdAsync(
        IReadOnlyList<int> contactIds,
        CrmDbContext db,
        CancellationToken cancellationToken)
    {
        var deals = await db.Deals.AsNoTracking()
            .Where(d => d.ContactId != null && contactIds.Contains(d.ContactId.Value))
            .OrderByDescending(d => d.Amount).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

        return deals.ToLookup(d => d.ContactId!.Value);
    }

    [DataLoader]
    public static async Task<ILookup<int, Activity>> GetActivitiesByContactIdAsync(
        IReadOnlyList<int> contactIds,
        CrmDbContext db,
        CancellationToken cancellationToken)
    {
        var activities = await db.Activities.AsNoTracking()
            .Where(a => a.ContactId != null && contactIds.Contains(a.ContactId.Value))
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

        return activities.ToLookup(a => a.ContactId!.Value);
    }

    [DataLoader]
    public static async Task<ILookup<int, Activity>> GetActivitiesByDealIdAsync(
        IReadOnlyList<int> dealIds,
        CrmDbContext db,
        CancellationToken cancellationToken)
    {
        var activities = await db.Activities.AsNoTracking()
            .Where(a => a.DealId != null && dealIds.Contains(a.DealId.Value))
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

        return activities.ToLookup(a => a.DealId!.Value);
    }

    [DataLoader]
    public static async Task<ILookup<int, CrmTask>> GetTasksByContactIdAsync(
        IReadOnlyList<int> contactIds,
        CrmDbContext db,
        CancellationToken cancellationToken)
    {
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.ContactId != null && contactIds.Contains(t.ContactId.Value))
            .OrderBy(t => t.IsCompleted).ThenBy(t => t.DueDate).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        return tasks.ToLookup(t => t.ContactId!.Value);
    }

    [DataLoader]
    public static async Task<ILookup<int, CrmTask>> GetTasksByDealIdAsync(
        IReadOnlyList<int> dealIds,
        CrmDbContext db,
        CancellationToken cancellationToken)
    {
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DealId != null && dealIds.Contains(t.DealId.Value))
            .OrderBy(t => t.IsCompleted).ThenBy(t => t.DueDate).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        return tasks.ToLookup(t => t.DealId!.Value);
    }
}

internal static class DataLoaderReloadExtensions
{
    /// <summary>
    /// Загружает сущность заново после того, как мутация её изменила. Мутации одного запроса выполняются
    /// по очереди и делят кэш DataLoader, поэтому без сброса следующая мутация могла бы вернуть старую версию.
    /// </summary>
    public static Task<TValue> ReloadAsync<TKey, TValue>(
        this IDataLoader<TKey, TValue> dataLoader,
        TKey key,
        CancellationToken cancellationToken)
        where TKey : notnull
        where TValue : notnull
    {
        dataLoader.RemoveCacheEntry(key);
        return dataLoader.LoadRequiredAsync(key, cancellationToken);
    }
}
