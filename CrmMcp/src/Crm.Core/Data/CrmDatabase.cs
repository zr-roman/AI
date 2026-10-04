using Crm.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crm.Core.Data;

public static class CrmDatabase
{
    /// <summary>Создаёт или мигрирует схему и при необходимости заполняет демо-данными.</summary>
    public static async Task InitializeCrmDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<CrmDbContext>();
        var options = provider.GetRequiredService<IOptions<CrmOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Crm.Database");

        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            // Быстрый старт без миграций. Как только появится первая миграция
            // (dotnet ef migrations add InitialCreate), здесь автоматически включится MigrateAsync.
            await db.Database.EnsureCreatedAsync(ct);
        }

        if (options.SeedDemoData && !await db.Users.AnyAsync(ct))
        {
            await CrmSeeder.SeedAsync(db, provider.GetRequiredService<CrmClock>(), ct);
            logger.LogInformation("Demo data seeded: anna@crm.local (Admin), boris@crm.local (Manager)");
        }
    }
}
