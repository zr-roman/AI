using Crm.Core.Data;
using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.Filtering;
using Crm.GraphQL.Infrastructure;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Data.Sorting;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace Crm.GraphQL.Admin;

// Поля только для администратора. [Authorize] — из HotChocolate.Authorization, а не из ASP.NET Core:
// проверка выполняется для конкретного поля, менеджер получит ошибку AUTH_NOT_AUTHORIZED, а остальная часть
// запроса выполнится. Права дополнительно проверяет сервис — так же, как для REST и MCP.
[QueryType]
public static partial class AdminQueries
{
    [Authorize(Roles = [nameof(UserRole.Admin)])]
    [GraphQLDescription("Журнал аудита: вызовы MCP-инструментов агентом — кто, что, с какими аргументами и чем закончилось. Только для администратора.")]
    [UsePaging]
    [UseFiltering(typeof(AuditEntryFilterInputType))]
    [UseSorting(typeof(AuditEntrySortInputType))]
    public static IQueryable<AuditEntry> GetAuditLog(CrmDbContext db, ISortingContext sorting)
    {
        sorting.UseDefaultOrder<AuditEntry, long>(q => q.OrderByDescending(e => e.OccurredAt), e => e.Id);
        return db.AuditLog.AsNoTracking();
    }

    [Authorize(Roles = [nameof(UserRole.Admin)])]
    [GraphQLDescription(
        "Шаг 1 массового закрытия: зависшие сделки и токен подтверждения. Ничего не меняет. " +
        "Шаг 2 — мутация closeStaleDeals с этим токеном. Только для администратора.")]
    public static async Task<StaleDealsPreview> GetStaleDealsClosurePreviewAsync(
        DealService deals,
        CancellationToken cancellationToken,
        [GraphQLDescription("Сколько дней без движения считать зависанием, от 7 до 365")] int inactiveDays = 30)
        => await deals.PreviewStaleDealsAsync(inactiveDays, cancellationToken);
}
