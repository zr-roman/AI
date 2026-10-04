using Crm.Core.Data;
using Crm.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crm.Core.Services;

/// <summary>
/// Пишет аудит в отдельном DbContext: если бизнес-операция упала посреди SaveChanges,
/// её незавершённые изменения не должны «доехать» до БД вместе с записью аудита.
/// </summary>
public sealed class AuditLog(IDbContextFactory<CrmDbContext> dbFactory, ILogger<AuditLog> logger)
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AuditLog.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Сбой аудита не должен ломать ответ пользователю, но обязан попасть в логи
            logger.LogError(ex, "Failed to write audit entry for {Operation}", entry.Operation);
        }
    }
}
